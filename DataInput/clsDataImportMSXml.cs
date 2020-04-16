﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using MASIC.DataOutput;
using MASICPeakFinder;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using MSDataFileReader;
using PRISM;
using PSI_Interface.CV;
using PSI_Interface.MSData;
using ThermoRawFileReader;

namespace MASIC.DataInput
{
    /// <summary>
    /// Import data from .mzXML, .mzData, or .mzML files
    /// </summary>
    public class clsDataImportMSXml : clsDataImport
    {
        #region "Member variables"
        private readonly Centroider mCentroider;
        private int mWarnCount;
        private int mMostRecentPrecursorScan;
        private readonly List<double> mCentroidedPrecursorIonsMz = new List<double>();
        private readonly List<double> mCentroidedPrecursorIonsIntensity = new List<double>();
        #endregion
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="masicOptions"></param>
        /// <param name="peakFinder"></param>
        /// <param name="parentIonProcessor"></param>
        /// <param name="scanTracking"></param>
        public clsDataImportMSXml(clsMASICOptions masicOptions, clsMASICPeakFinder peakFinder, clsParentIonProcessing parentIonProcessor, clsScanTracking scanTracking) : base(masicOptions, peakFinder, parentIonProcessor, scanTracking)
        {
            mCentroider = new Centroider();
        }

        private double ComputeInterference(SimpleMzMLReader.SimpleSpectrum mzMLSpectrum, clsScanInfo scanInfo, int precursorScanNumber)
        {
            if (mzMLSpectrum == null)
            {
                return 0;
            }

            if (precursorScanNumber != mCachedPrecursorScan)
            {
                if (mMostRecentPrecursorScan != precursorScanNumber)
                {
                    ReportWarning(string.Format("Most recent precursor scan is {0}, and not {1}; cannot compute interference for scan {2}", mMostRecentPrecursorScan, precursorScanNumber, scanInfo.ScanNumber));
                    return 0;
                }

                UpdateCachedPrecursorScan(mMostRecentPrecursorScan, mCentroidedPrecursorIonsMz, mCentroidedPrecursorIonsIntensity);
            }

            double isolationWidth = 0;
            int chargeState = 0;
            string chargeStateText = string.Empty;

            // This is only used if scanInfo.FragScanInfo.ParentIonMz is zero
            string monoMzText = string.Empty;
            string isolationWidthText = string.Empty;
            string isolationWindowTargetMzText = string.Empty;
            string isolationWindowLowerOffsetText = string.Empty;
            string isolationWindowUpperOffsetText = string.Empty;
            if (mzMLSpectrum.Precursors.Count > 0 && mzMLSpectrum.Precursors[0].IsolationWindow != null)
            {
                foreach (var cvParam in mzMLSpectrum.Precursors[0].IsolationWindow.CVParams)
                {
                    switch (cvParam.TermInfo.Cvid)
                    {
                        case CV.CVID.MS_isolation_width_OBSOLETE:
                            isolationWidthText = cvParam.Value;
                            break;
                        case CV.CVID.MS_isolation_window_target_m_z:
                            isolationWindowTargetMzText = cvParam.Value;
                            break;
                        case CV.CVID.MS_isolation_window_lower_offset:
                            isolationWindowLowerOffsetText = cvParam.Value;
                            break;
                        case CV.CVID.MS_isolation_window_upper_offset:
                            isolationWindowUpperOffsetText = cvParam.Value;
                            break;
                    }
                }
            }

            if (mzMLSpectrum.Precursors.Count > 0 && mzMLSpectrum.Precursors[0].SelectedIons != null && mzMLSpectrum.Precursors[0].SelectedIons.Count > 0)
            {
                foreach (var cvParam in mzMLSpectrum.Precursors[0].SelectedIons[0].CVParams)
                {
                    switch (cvParam.TermInfo.Cvid)
                    {
                        case CV.CVID.MS_selected_ion_m_z:
                        case CV.CVID.MS_selected_precursor_m_z:
                            monoMzText = cvParam.Value;
                            break;
                        case CV.CVID.MS_charge_state:
                            chargeStateText = cvParam.Value;
                            break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(chargeStateText))
            {
                if (!int.TryParse(chargeStateText, out chargeState))
                {
                    chargeState = 0;
                }
            }

            bool isolationWidthDefined = false;
            if (!string.IsNullOrWhiteSpace(isolationWidthText))
            {
                if (double.TryParse(isolationWidthText, out isolationWidth))
                {
                    isolationWidthDefined = true;
                }
            }

            if (!isolationWidthDefined && !string.IsNullOrWhiteSpace(isolationWindowTargetMzText))
            {
                double isolationWindowTargetMz;
                double isolationWindowLowerOffset;
                double isolationWindowUpperOffset;
                if (double.TryParse(isolationWindowTargetMzText, out isolationWindowTargetMz) && double.TryParse(isolationWindowLowerOffsetText, out isolationWindowLowerOffset) && double.TryParse(isolationWindowUpperOffsetText, out isolationWindowUpperOffset))
                {
                    isolationWidth = isolationWindowLowerOffset + isolationWindowUpperOffset;
                    isolationWidthDefined = true;
                }
                else
                {
                    WarnIsolationWidthNotFound(scanInfo.ScanNumber, string.Format("Could not determine the MS2 isolation width; unable to parse {0}", isolationWindowLowerOffsetText));
                }
            }
            else
            {
                WarnIsolationWidthNotFound(scanInfo.ScanNumber, string.Format("Could not determine the MS2 isolation width (CVParam '{0}' not found)", "isolation window target m/z"));
            }

            if (!isolationWidthDefined)
            {
                return 0;
            }

            double parentIonMz;
            if (Math.Abs(scanInfo.FragScanInfo.ParentIonMz) > 0)
            {
                parentIonMz = scanInfo.FragScanInfo.ParentIonMz;
            }
            else
            {
                // The mzML reader could not determine the parent ion m/z value (this is highly unlikely)
                // Use scan event "Monoisotopic M/Z" instead

                if (string.IsNullOrWhiteSpace(monoMzText))
                {
                    ReportWarning("Could not determine the parent ion m/z value via CV param 'selected ion m/z'" + "cannot compute interference for scan " + scanInfo.ScanNumber);
                    return 0;
                }

                double mz;
                if (!double.TryParse(monoMzText, out mz))
                {
                    OnWarningEvent(string.Format("Skipping scan {0} since 'selected ion m/z' was not a number:  {1}", scanInfo.ScanNumber, monoMzText));
                    return 0;
                }

                parentIonMz = mz;
            }

            if (Math.Abs(parentIonMz) < float.Epsilon)
            {
                ReportWarning("Parent ion m/z is 0; cannot compute interference for scan " + scanInfo.ScanNumber);
                return 0;
            }

            double precursorInterference = ComputePrecursorInterference(scanInfo.ScanNumber, precursorScanNumber, parentIonMz, isolationWidth, chargeState);
            return precursorInterference;
        }

        public bool ExtractScanInfoFromMzMLDataFile(string filePath, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            try
            {
                var msXmlFileInfo = new FileInfo(filePath);
                return ExtractScanInfoFromMzMLDataFile(msXmlFileInfo, scanList, spectraCache, dataOutputHandler, keepRawSpectra, keepMSMSSpectra);
            }
            catch (Exception ex)
            {
                ReportError("Error in ExtractScanInfoFromMzMLDataFile", ex, clsMASIC.eMasicErrorCodes.InputFileDataReadError);
                return false;
            }
        }

        public bool ExtractScanInfoFromMzXMLDataFile(string filePath, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            clsMSDataFileReaderBaseClass xmlReader;
            try
            {
                xmlReader = new clsMzXMLFileReader();
                return ExtractScanInfoFromMSXMLDataFile(filePath, xmlReader, scanList, spectraCache, dataOutputHandler, keepRawSpectra, keepMSMSSpectra);
            }
            catch (Exception ex)
            {
                ReportError("Error in ExtractScanInfoFromMzXMLDataFile", ex, clsMASIC.eMasicErrorCodes.InputFileDataReadError);
                return false;
            }
        }

        public bool ExtractScanInfoFromMzDataFile(string filePath, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            clsMSDataFileReaderBaseClass xmlReader;
            try
            {
                xmlReader = new clsMzDataFileReader();
                return ExtractScanInfoFromMSXMLDataFile(filePath, xmlReader, scanList, spectraCache, dataOutputHandler, keepRawSpectra, keepMSMSSpectra);
            }
            catch (Exception ex)
            {
                ReportError("Error in ExtractScanInfoFromMzDataFile", ex, clsMASIC.eMasicErrorCodes.InputFileDataReadError);
                return false;
            }
        }

        private bool ExtractScanInfoFromMSXMLDataFile(string filePath, clsMSDataFileReaderBaseClass xmlReader, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            // Returns True if Success, False if failure
            // Note: This function assumes filePath exists

            bool success;
            try
            {
                Console.Write("Reading MSXml data file ");
                ReportMessage("Reading MSXml data file");
                UpdateProgress(0, "Opening data file:" + ControlChars.NewLine + Path.GetFileName(filePath));

                // Obtain the full path to the file
                var msXmlFileInfo = new FileInfo(filePath);
                string inputFileFullPath = msXmlFileInfo.FullName;
                int datasetID = mOptions.SICOptions.DatasetID;
                bool fileStatsSuccess = UpdateDatasetFileStats(msXmlFileInfo, datasetID);
                if (!fileStatsSuccess)
                {
                    return false;
                }

                mDatasetFileInfo.ScanCount = 0;

                // Open a handle to the data file
                if (!xmlReader.OpenFile(inputFileFullPath))
                {
                    ReportError("Error opening input data file: " + inputFileFullPath);
                    SetLocalErrorCode(clsMASIC.eMasicErrorCodes.InputFileAccessError);
                    return false;
                }

                InitOptions(scanList, keepRawSpectra, keepMSMSSpectra);
                UpdateProgress("Reading XML data" + ControlChars.NewLine + Path.GetFileName(filePath));
                ReportMessage("Reading XML data from " + filePath);
                double scanTimeMax = 0;
                while (true)
                {
                    clsSpectrumInfo spectrumInfo = null;
                    bool scanFound = xmlReader.ReadNextSpectrum(out spectrumInfo);
                    if (!scanFound)
                        break;
                    mDatasetFileInfo.ScanCount += 1;
                    scanTimeMax = spectrumInfo.RetentionTimeMin;
                    if (spectrumInfo.ScanNumber > 0 && !mScanTracking.CheckScanInRange(spectrumInfo.ScanNumber, mOptions.SICOptions))
                    {
                        mScansOutOfRange += 1;
                        continue;
                    }

                    var msSpectrum = new clsMSSpectrum(spectrumInfo.ScanNumber, spectrumInfo.MZList, spectrumInfo.IntensityList, spectrumInfo.DataCount);
                    float percentComplete = xmlReader.ProgressPercentComplete;
                    SimpleMzMLReader.SimpleSpectrum nullMzMLSpectrum = null;
                    bool extractSuccess = ExtractScanInfoCheckRange(msSpectrum, spectrumInfo, nullMzMLSpectrum, scanList, spectraCache, dataOutputHandler, percentComplete, mDatasetFileInfo.ScanCount);
                    if (!extractSuccess)
                    {
                        break;
                    }
                }

                mDatasetFileInfo.AcqTimeEnd = mDatasetFileInfo.AcqTimeStart.AddMinutes(scanTimeMax);

                // Shrink the memory usage of the scanList arrays
                success = FinalizeScanList(scanList, msXmlFileInfo);
            }
            catch (Exception ex)
            {
                ReportError("Error in ExtractScanInfoFromMSXMLDataFile", ex, clsMASIC.eMasicErrorCodes.InputFileDataReadError);
                success = false;
            }

            // Close the handle to the data file
            if (xmlReader != null)
            {
                try
                {
                    xmlReader.CloseFile();
                }
                catch (Exception ex)
                {
                    // Ignore errors here
                }
            }

            return success;
        }

        private bool ExtractScanInfoFromMzMLDataFile(FileInfo mzMLFile, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            bool fileOpened = false;
            try
            {
                Console.Write("Reading MSXml data file ");
                ReportMessage("Reading MSXml data file");
                UpdateProgress(0, "Opening data file:" + ControlChars.NewLine + mzMLFile.Name);
                int datasetID = mOptions.SICOptions.DatasetID;
                if (!mzMLFile.Exists)
                {
                    return false;
                }

                mDatasetFileInfo.ScanCount = 0;

                // Open a handle to the data file
                var xmlReader = new SimpleMzMLReader(mzMLFile.FullName, false);
                fileOpened = true;
                bool fileStatsSuccess = UpdateDatasetFileStats(mzMLFile, datasetID, xmlReader);
                if (!fileStatsSuccess)
                {
                    return false;
                }

                InitOptions(scanList, keepRawSpectra, keepMSMSSpectra);
                bool thermoRawFile = false;
                foreach (var cvParam in xmlReader.SourceFileParams.CVParams)
                {
                    switch (cvParam.TermInfo.Cvid)
                    {
                        case CV.CVID.MS_Thermo_nativeID_format:
                        case CV.CVID.MS_Thermo_nativeID_format__combined_spectra:
                        case CV.CVID.MS_Thermo_RAW_format:
                            thermoRawFile = true;
                            break;
                    }
                }

                UpdateProgress("Reading XML data" + ControlChars.NewLine + mzMLFile.Name);
                ReportMessage("Reading XML data from " + mzMLFile.FullName);
                double scanTimeMax = 0;
                if (xmlReader.NumSpectra > 0)
                {
                    var iterator = xmlReader.ReadAllSpectra(true).GetEnumerator();
                    while (iterator.MoveNext())
                    {
                        var mzMLSpectrum = iterator.Current;
                        mDatasetFileInfo.ScanCount += 1;
                        if (mzMLSpectrum.ScanNumber > 0 && !mScanTracking.CheckScanInRange(mzMLSpectrum.ScanNumber, mOptions.SICOptions))
                        {
                            mScansOutOfRange += 1;
                            continue;
                        }

                        var mzList = mzMLSpectrum.Mzs.ToList();
                        var intensityList = mzMLSpectrum.Intensities.ToList();
                        var mzXmlSourceSpectrum = GetSpectrumInfoFromMzMLSpectrum(mzMLSpectrum, mzList, intensityList, thermoRawFile);
                        scanTimeMax = mzXmlSourceSpectrum.RetentionTimeMin;
                        var msSpectrum = new clsMSSpectrum(mzXmlSourceSpectrum.ScanNumber, mzList, intensityList, mzList.Count);
                        double percentComplete = scanList.MasterScanOrderCount / Conversions.ToDouble(xmlReader.NumSpectra) * 100;
                        bool extractSuccess = ExtractScanInfoCheckRange(msSpectrum, mzXmlSourceSpectrum, mzMLSpectrum, scanList, spectraCache, dataOutputHandler, percentComplete, mDatasetFileInfo.ScanCount);
                        if (!extractSuccess)
                        {
                            break;
                        }
                    }

                    mDatasetFileInfo.AcqTimeEnd = mDatasetFileInfo.AcqTimeStart.AddMinutes(scanTimeMax);
                }
                else if (xmlReader.NumSpectra == 0 && xmlReader.NumChromatograms > 0)
                {
                    var iterator1 = xmlReader.ReadAllChromatograms(true).GetEnumerator();

                    // Construct a list of the difference in time (in minutes) between adjacent data points in each chromatogram
                    var scanTimeDiffMedians = new List<double>();

                    // Also keep track of elution times
                    // Keys in this dictionary are chromatogram number
                    // Values are a dictionary where keys are elution times and values are the pseudo scan number mapped to each time (initially 0)
                    var elutionTimeToScanMapByChromatogram = new Dictionary<int, SortedDictionary<double, int>>();
                    int chromatogramNumber = 0;
                    while (iterator1.MoveNext())
                    {
                        var chromatogramItem = iterator1.Current;
                        bool isSRM = IsSrmChromatogram(chromatogramItem);
                        if (!isSRM)
                            continue;
                        chromatogramNumber += 1;
                        var elutionTimeToScanMap = new SortedDictionary<double, int>();
                        elutionTimeToScanMapByChromatogram.Add(chromatogramNumber, elutionTimeToScanMap);

                        // Construct a list of the difference in time (in minutes) between adjacent data points in each chromatogram
                        var scanTimeDiffs = new List<double>();
                        var scanTimes = chromatogramItem.Times.ToList();
                        for (int i = 0; i <= scanTimes.Count - 1; i++)
                        {
                            if (!elutionTimeToScanMap.ContainsKey(scanTimes[i]))
                            {
                                elutionTimeToScanMap.Add(scanTimes[i], 0);
                            }

                            if (i > 0)
                            {
                                var adjacentTimeDiff = scanTimes[i] - scanTimes[i - 1];
                                if (adjacentTimeDiff > 0)
                                {
                                    scanTimeDiffs.Add(adjacentTimeDiff);
                                }
                            }
                        }

                        // First, compute the median time diff in scanTimeDiffs
                        double medianScanTimeDiffThisChromatogram = clsUtilities.ComputeMedian(scanTimeDiffs);
                        scanTimeDiffMedians.Add(medianScanTimeDiffThisChromatogram);
                    }

                    // Construct a mapping between elution time and scan number
                    // This is a bit of a challenge since chromatogram data only tracks elution time, and not scan number

                    // First, compute the overall median time diff
                    double medianScanTimeDiff = clsUtilities.ComputeMedian(scanTimeDiffMedians);
                    if (Math.Abs(medianScanTimeDiff) < 0.000001)
                    {
                        medianScanTimeDiff = 0.000001;
                    }

                    Console.WriteLine("Populating dictionary mapping SRM ion elution times to pseudo scan number");
                    var lastProgress = DateTime.UtcNow;
                    int chromatogramsProcessed = 0;

                    // Keys in this dictionary are scan times, values are the pseudo scan number mapped to each time
                    var elutionTimeToScanMapMaster = new Dictionary<double, int>();

                    // Define the mapped scan number fo each elution time in elutionTimeToScanMapByChromatogram
                    foreach (var chromTimesEntry in elutionTimeToScanMapByChromatogram)
                    {
                        if (DateTime.UtcNow.Subtract(lastProgress).TotalSeconds >= 2.5)
                        {
                            lastProgress = DateTime.UtcNow;
                            double percentComplete = chromatogramsProcessed / Conversions.ToDouble(elutionTimeToScanMapByChromatogram.Count) * 100;
                            Console.Write("{0:N0}% ", percentComplete);
                        }

                        var elutionTimeToScanMap = chromTimesEntry.Value;
                        foreach (var elutionTime in elutionTimeToScanMap.Keys.ToList())
                        {
                            int nearestPseudoScan = Conversions.ToInteger(Math.Round(elutionTime / medianScanTimeDiff * 100)) + 1;
                            elutionTimeToScanMap[elutionTime] = nearestPseudoScan;
                        }

                        // Fix duplicate scans (values) in elutionTimeToScanMap, if possible
                        // For Each elutionTime In (From item In elutionTimeToScanMap.Keys Order By item Select item)

                        for (int i = 1; i <= elutionTimeToScanMap.Count - 1; i++)
                        {
                            int previousScan = elutionTimeToScanMap.Values.ElementAtOrDefault(i - 1);
                            int currentScan = elutionTimeToScanMap.Values.ElementAtOrDefault(i);
                            if (currentScan == previousScan)
                            {
                                // Adjacent time points have an identical scan number
                                if (i == elutionTimeToScanMap.Count - 1)
                                {
                                    elutionTimeToScanMap[elutionTimeToScanMap.Keys.ElementAtOrDefault(i)] = currentScan + 1;
                                }
                                else
                                {
                                    int nextScan = elutionTimeToScanMap.Values.ElementAtOrDefault(i + 1);
                                    if (nextScan - currentScan > 1)
                                    {
                                        // The next scan is more than 1 scan away from this one; it is safe to increment currentScan
                                        elutionTimeToScanMap[elutionTimeToScanMap.Keys.ElementAtOrDefault(i)] = currentScan + 1;
                                    }
                                }
                            }
                        }

                        // Populate the master dictionary mapping elution time to scan number
                        int existingScan;
                        foreach (var item in elutionTimeToScanMap)
                        {
                            if (elutionTimeToScanMapMaster.TryGetValue(item.Key, out existingScan))
                            {
                                if (existingScan != item.Value)
                                {
                                    Console.WriteLine("Elution times resulted in different pseudo scans; this is unexpected");
                                }
                            }
                            else
                            {
                                elutionTimeToScanMapMaster.Add(item.Key, item.Value);
                            }
                        }

                        chromatogramsProcessed += 1;
                    }

                    Console.WriteLine();

                    // Keys in this dictionary are scan numbers
                    // Values are a dictionary tracking m/z values and intensities
                    var simulatedSpectraByScan = new Dictionary<int, Dictionary<double, double>>();

                    // Keys in this dictionary are scan numbers
                    // Values are the elution time for the scan
                    var simulatedSpectraTimes = new Dictionary<int, double>();
                    var xmlReader2 = new SimpleMzMLReader(mzMLFile.FullName, false);
                    var iterator2 = xmlReader2.ReadAllChromatograms(true).GetEnumerator();
                    int scanTimeLookupErrors = 0;
                    int nextWarningThreshold = 10;
                    while (iterator2.MoveNext())
                    {
                        var chromatogramItem = iterator2.Current;
                        bool isSRM = IsSrmChromatogram(chromatogramItem);
                        if (!isSRM)
                            continue;
                        var scanTimes = chromatogramItem.Times.ToList();
                        var intensities = chromatogramItem.Intensities.ToList();
                        var currentMz = chromatogramItem.Product.TargetMz;
                        int scanToStore;
                        for (int i = 0; i <= scanTimes.Count - 1; i++)
                        {
                            if (elutionTimeToScanMapMaster.TryGetValue(scanTimes[i], out scanToStore))
                            {
                                bool success = StoreSimulatedDataPoint(simulatedSpectraByScan, simulatedSpectraTimes, scanToStore, scanTimes[i], currentMz, intensities[i]);
                                if (!success && scanToStore > 1)
                                {
                                    // The current scan already has a value for this m/z
                                    // Try storing in the previous scan
                                    success = StoreSimulatedDataPoint(simulatedSpectraByScan, simulatedSpectraTimes, scanToStore - 1, scanTimes[i], currentMz, intensities[i]);
                                }

                                if (!success)
                                {
                                    // The current scan and the previous scan already have a value for this m/z
                                    // Store in the next scan
                                    StoreSimulatedDataPoint(simulatedSpectraByScan, simulatedSpectraTimes, scanToStore + 1, scanTimes[i], currentMz, intensities[i]);
                                }

                                if (!success)
                                {
                                    ConsoleMsgUtils.ShowDebug("Skipping duplicate m/z value {0} for scan {1}", currentMz, scanToStore);
                                }
                            }
                            else
                            {
                                scanTimeLookupErrors += 1;
                                if (scanTimeLookupErrors <= 5 || scanTimeLookupErrors >= nextWarningThreshold)
                                {
                                    ConsoleMsgUtils.ShowWarning("The elutionTimeToScanMap dictionary did not have scan time {0:N1} for {1}; this is unexpected", scanTimes[i], chromatogramItem.Id);
                                    if (scanTimeLookupErrors > 5)
                                    {
                                        nextWarningThreshold *= 2;
                                    }
                                }
                            }
                        }
                    }

                    // Call ExtractFragmentationScan for each scan in simulatedSpectraByScan

                    var mzList = new List<double>();
                    var intensityList = new List<double>();
                    foreach (var simulatedSpectrum in simulatedSpectraByScan)
                    {
                        int scanNumber = simulatedSpectrum.Key;
                        mDatasetFileInfo.ScanCount += 1;
                        if (scanNumber > 0 && !mScanTracking.CheckScanInRange(scanNumber, mOptions.SICOptions))
                        {
                            mScansOutOfRange += 1;
                            continue;
                        }

                        string nativeId = string.Format("controllerType=0 controllerNumber=1 scan={0}", scanNumber);
                        double scanStartTime = simulatedSpectraTimes[scanNumber];
                        var cvParams = new List<SimpleMzMLReader.CVParamData>();
                        var userParams = new List<SimpleMzMLReader.UserParamData>();
                        var precursors = new List<SimpleMzMLReader.Precursor>();
                        var scanWindows = new List<SimpleMzMLReader.ScanWindowData>();
                        mzList.Clear();
                        intensityList.Clear();
                        foreach (var dataPoint in simulatedSpectrum.Value)
                        {
                            mzList.Add(dataPoint.Key);
                            intensityList.Add(dataPoint.Value);
                        }

                        var mzMLSpectrum = new SimpleMzMLReader.SimpleSpectrum(mzList, intensityList, scanNumber, nativeId, scanStartTime, cvParams, userParams, precursors, scanWindows);
                        var mzXmlSourceSpectrum = GetSpectrumInfoFromMzMLSpectrum(mzMLSpectrum, mzList, intensityList, thermoRawFile);
                        scanTimeMax = mzXmlSourceSpectrum.RetentionTimeMin;
                        var msSpectrum = new clsMSSpectrum(mzXmlSourceSpectrum.ScanNumber, mzList, intensityList, mzList.Count);
                        double percentComplete = scanList.MasterScanOrderCount / Conversions.ToDouble(simulatedSpectraByScan.Count) * 100;
                        bool extractSuccess = ExtractScanInfoCheckRange(msSpectrum, mzXmlSourceSpectrum, mzMLSpectrum, scanList, spectraCache, dataOutputHandler, percentComplete, mDatasetFileInfo.ScanCount);
                        if (!extractSuccess)
                        {
                            break;
                        }
                    }

                    mDatasetFileInfo.AcqTimeEnd = mDatasetFileInfo.AcqTimeStart.AddMinutes(scanTimeMax);
                }

                // Shrink the memory usage of the scanList arrays
                bool finalizeSuccess = FinalizeScanList(scanList, mzMLFile);
                return finalizeSuccess;
            }
            catch (Exception ex)
            {
                if (!fileOpened)
                {
                    ReportError("Error opening input data file: " + mzMLFile.FullName);
                    SetLocalErrorCode(clsMASIC.eMasicErrorCodes.InputFileAccessError);
                    return false;
                }

                ReportError("Error in ExtractScanInfoFromMzMLDataFile", ex, clsMASIC.eMasicErrorCodes.InputFileDataReadError);
                return false;
            }
        }

        private bool ExtractScanInfoCheckRange(clsMSSpectrum msSpectrum, clsSpectrumInfo spectrumInfo, SimpleMzMLReader.SimpleSpectrum mzMLSpectrum, clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, double percentComplete, int scansRead)
        {
            bool success;
            if (mScanTracking.CheckScanInRange(spectrumInfo.ScanNumber, spectrumInfo.RetentionTimeMin, mOptions.SICOptions))
            {
                success = ExtractScanInfoWork(scanList, spectraCache, dataOutputHandler, mOptions.SICOptions, msSpectrum, spectrumInfo, mzMLSpectrum);
            }
            else
            {
                mScansOutOfRange += 1;
                success = true;
            }

            if (!double.IsNaN(percentComplete))
            {
                UpdateProgress(Conversions.ToShort(Math.Round(percentComplete, 0)));
            }

            UpdateCacheStats(spectraCache);
            if (mOptions.AbortProcessing)
            {
                scanList.ProcessingIncomplete = true;
                return false;
            }

            if (DateTime.UtcNow.Subtract(mLastLogTime).TotalSeconds >= 10 || scansRead % 500 == 0)
            {
                ReportMessage("Reading scan: " + scansRead.ToString());
                Console.Write(".");
                mLastLogTime = DateTime.UtcNow;
            }

            return success;
        }

        private bool ExtractScanInfoWork(clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, clsSICOptions sicOptions, clsMSSpectrum msSpectrum, clsSpectrumInfo spectrumInfo, SimpleMzMLReader.SimpleSpectrum mzMLSpectrum)
        {
            bool isMzXML;
            clsSpectrumInfoMzXML mzXmlSourceSpectrum = null;

            // Note that both mzXML and mzML data is stored in spectrumInfo
            if (spectrumInfo is clsSpectrumInfoMzXML)
            {
                mzXmlSourceSpectrum = (clsSpectrumInfoMzXML)spectrumInfo;
                isMzXML = true;
            }
            else
            {
                isMzXML = false;
            }

            bool success;

            // Determine if this was an MS/MS scan
            // If yes, determine the scan number of the survey scan
            if (spectrumInfo.MSLevel <= 1)
            {
                // Survey Scan
                success = ExtractSurveyScan(scanList, spectraCache, dataOutputHandler, spectrumInfo, msSpectrum, sicOptions, isMzXML, mzXmlSourceSpectrum);
            }
            else
            {
                // Fragmentation Scan
                success = ExtractFragmentationScan(scanList, spectraCache, dataOutputHandler, spectrumInfo, msSpectrum, sicOptions, isMzXML, mzXmlSourceSpectrum, mzMLSpectrum);
            }

            return success;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="scanList"></param>
        /// <param name="spectraCache"></param>
        /// <param name="dataOutputHandler"></param>
        /// <param name="spectrumInfo"></param>
        /// <param name="msSpectrum">Tracks scan number, m/z values, and intensity values; msSpectrum.IonCount is the number of data points</param>
        /// <param name="sicOptions"></param>
        /// <param name="isMzXML"></param>
        /// <param name="mzXmlSourceSpectrum"></param>
        /// <returns></returns>
        private bool ExtractSurveyScan(clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, clsSpectrumInfo spectrumInfo, clsMSSpectrum msSpectrum, clsSICOptions sicOptions, bool isMzXML, clsSpectrumInfoMzXML mzXmlSourceSpectrum)
        {
            var scanInfo = new clsScanInfo()
            {
                ScanNumber = spectrumInfo.ScanNumber,
                ScanTime = spectrumInfo.RetentionTimeMin,
                ScanHeaderText = string.Empty,
                ScanTypeName = "MS",    // This may get updated via the call to UpdateMSXmlScanType()
                BasePeakIonMZ = spectrumInfo.BasePeakMZ,
                BasePeakIonIntensity = spectrumInfo.BasePeakIntensity,
                TotalIonIntensity = spectrumInfo.TotalIonCurrent,
                MinimumPositiveIntensity = 0,
                ZoomScan = false,
                SIMScan = false,
                MRMScanType = MRMScanTypeConstants.NotMRM,
                LowMass = spectrumInfo.mzRangeStart,
                HighMass = spectrumInfo.mzRangeEnd
            };
            if (mzXmlSourceSpectrum != null && !string.IsNullOrWhiteSpace(mzXmlSourceSpectrum.FilterLine))
            {
                scanInfo.IsFTMS = IsHighResolutionSpectrum(mzXmlSourceSpectrum.FilterLine);
            }

            // Survey scans typically lead to multiple parent ions; we do not record them here
            scanInfo.FragScanInfo.ParentIonInfoIndex = -1;

            // Determine the minimum positive intensity in this scan
            scanInfo.MinimumPositiveIntensity = mPeakFinder.FindMinimumPositiveValue(msSpectrum.IonsIntensity, 0);
            scanList.SurveyScans.Add(scanInfo);
            UpdateMSXmlScanType(scanInfo, spectrumInfo.MSLevel, "MS", isMzXML, mzXmlSourceSpectrum);
            if (!scanInfo.ZoomScan)
            {
                mLastNonZoomSurveyScanIndex = scanList.SurveyScans.Count - 1;
            }

            scanList.AddMasterScanEntry(clsScanList.eScanTypeConstants.SurveyScan, scanList.SurveyScans.Count - 1);
            mLastSurveyScanIndexInMasterSeqOrder = scanList.MasterScanOrderCount - 1;
            double msDataResolution;
            if (mOptions.SICOptions.SICToleranceIsPPM)
            {
                // Define MSDataResolution based on the tolerance value that will be used at the lowest m/z in this spectrum, divided by sicOptions.CompressToleranceDivisorForPPM
                // However, if the lowest m/z value is < 100, then use 100 m/z
                if (spectrumInfo.mzRangeStart < 100)
                {
                    msDataResolution = clsParentIonProcessing.GetParentIonToleranceDa(sicOptions, 100) / sicOptions.CompressToleranceDivisorForPPM;
                }
                else
                {
                    msDataResolution = clsParentIonProcessing.GetParentIonToleranceDa(sicOptions, spectrumInfo.mzRangeStart) / sicOptions.CompressToleranceDivisorForPPM;
                }
            }
            else
            {
                msDataResolution = sicOptions.SICTolerance / sicOptions.CompressToleranceDivisorForDa;
            }

            // Note: Even if keepRawSpectra = False, we still need to load the raw data so that we can compute the noise level for the spectrum
            StoreSpectrum(msSpectrum, scanInfo, spectraCache, sicOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions, clsMASIC.DISCARD_LOW_INTENSITY_MS_DATA_ON_LOAD, sicOptions.CompressMSSpectraData, msDataResolution, mKeepRawSpectra);
            if (msSpectrum.IonsMZ != null && msSpectrum.IonsIntensity != null)
            {
                if (mzXmlSourceSpectrum.Centroided)
                {
                    // Data is already centroided
                    UpdateCachedPrecursorScanData(scanInfo.ScanNumber, msSpectrum);
                }
                else
                {
                    // Need to centroid the data
                    double[] sourceMzs;
                    double[] sourceIntensities;
                    sourceMzs = new double[msSpectrum.IonCount];
                    sourceIntensities = new double[msSpectrum.IonCount];
                    for (int i = 0; i <= msSpectrum.IonCount - 1; i++)
                    {
                        sourceMzs[i] = msSpectrum.IonsMZ[i];
                        sourceIntensities[i] = msSpectrum.IonsIntensity[i];
                    }

                    double[] centroidedPrecursorIonsMz = null;
                    double[] centroidedPrecursorIonsIntensity = null;
                    double massResolution = mCentroider.EstimateResolution(1000, 0.5, scanInfo.IsFTMS);
                    bool centroidSuccess = mCentroider.CentroidData(scanInfo, sourceMzs, sourceIntensities, massResolution, out centroidedPrecursorIonsMz, out centroidedPrecursorIonsIntensity);
                    if (centroidSuccess)
                    {
                        mMostRecentPrecursorScan = scanInfo.ScanNumber;
                        mCentroidedPrecursorIonsMz.Clear();
                        mCentroidedPrecursorIonsIntensity.Clear();
                        for (int i = 0; i <= centroidedPrecursorIonsMz.Length - 1; i++)
                        {
                            mCentroidedPrecursorIonsMz.Add(centroidedPrecursorIonsMz[i]);
                            mCentroidedPrecursorIonsIntensity.Add(centroidedPrecursorIonsIntensity[i]);
                        }
                    }
                }
            }

            SaveScanStatEntry(dataOutputHandler.OutputFileHandles.ScanStats, clsScanList.eScanTypeConstants.SurveyScan, scanInfo, sicOptions.DatasetID);
            return true;
        }

        private bool ExtractFragmentationScan(clsScanList scanList, clsSpectraCache spectraCache, clsDataOutput dataOutputHandler, clsSpectrumInfo spectrumInfo, clsMSSpectrum msSpectrum, clsSICOptions sicOptions, bool isMzXML, clsSpectrumInfoMzXML mzXmlSourceSpectrum, SimpleMzMLReader.SimpleSpectrum mzMLSpectrum)
        {
            var scanInfo = new clsScanInfo(spectrumInfo.ParentIonMZ)
            {
                ScanNumber = spectrumInfo.ScanNumber,
                ScanTime = spectrumInfo.RetentionTimeMin,
                ScanHeaderText = string.Empty,
                ScanTypeName = "MSn",          // This may get updated via the call to UpdateMSXmlScanType()
                BasePeakIonMZ = spectrumInfo.BasePeakMZ,
                BasePeakIonIntensity = spectrumInfo.BasePeakIntensity,
                TotalIonIntensity = spectrumInfo.TotalIonCurrent,
                MinimumPositiveIntensity = 0,
                ZoomScan = false,
                SIMScan = false,
                MRMScanType = MRMScanTypeConstants.NotMRM
            };

            // 1 for the first MS/MS scan after the survey scan, 2 for the second one, etc.
            if (mLastSurveyScanIndexInMasterSeqOrder < 0)
            {
                // We have not yet read a survey scan; store 1 for the fragmentation scan number
                scanInfo.FragScanInfo.FragScanNumber = 1;
            }
            else
            {
                scanInfo.FragScanInfo.FragScanNumber = scanList.MasterScanOrderCount - 1 - mLastSurveyScanIndexInMasterSeqOrder;
            }

            scanInfo.FragScanInfo.MSLevel = spectrumInfo.MSLevel;
            scanInfo.FragScanInfo.CollisionMode = mzXmlSourceSpectrum.ActivationMethod;

            // Determine the minimum positive intensity in this scan
            scanInfo.MinimumPositiveIntensity = mPeakFinder.FindMinimumPositiveValue(msSpectrum.IonsIntensity, 0);
            UpdateMSXmlScanType(scanInfo, spectrumInfo.MSLevel, "MSn", isMzXML, mzXmlSourceSpectrum);
            var eMRMScanType = scanInfo.MRMScanType;
            if (!(eMRMScanType == MRMScanTypeConstants.NotMRM))
            {
                // This is an MRM scan
                scanList.MRMDataPresent = true;
                var mrmScan = new ThermoRawFileReader.clsScanInfo(spectrumInfo.SpectrumID)
                {
                    MRMScanType = eMRMScanType,
                    MRMInfo = new MRMInfo()
                };

                // Obtain the detailed filter string, e.g. "+ c NSI SRM ms2 495.285 [409.260-409.262, 506.329-506.331, 607.376-607.378]"
                // In contrast, scanInfo.ScanHeaderText has a truncated filter string, e.g. "+ c NSI SRM ms2"

                string filterString = GetFilterString(mzMLSpectrum);
                if (string.IsNullOrWhiteSpace(filterString))
                {
                    mrmScan.FilterText = scanInfo.ScanHeaderText;
                }
                else
                {
                    mrmScan.FilterText = filterString;
                }

                if (!string.IsNullOrEmpty(mrmScan.FilterText))
                {
                    // Parse out the MRM_QMS or SRM information for this scan
                    var argmrmInfo = mrmScan.MRMInfo;
                    XRawFileIO.ExtractMRMMasses(mrmScan.FilterText, mrmScan.MRMScanType, out argmrmInfo);
                }
                else
                {
                    // .MZRangeStart and .MZRangeEnd should be equivalent, and they should define the m/z of the MRM transition

                    if (spectrumInfo.mzRangeEnd - spectrumInfo.mzRangeStart >= 0.5)
                    {
                        // The data is likely MRM and not SRM
                        // We cannot currently handle data like this
                        // (would need to examine the mass values and find the clumps of data to infer the transitions present)
                        mWarnCount += 1;
                        if (mWarnCount <= 5)
                        {
                            ReportError("Warning: m/z range for SRM scan " + spectrumInfo.ScanNumber + " is " + (spectrumInfo.mzRangeEnd - spectrumInfo.mzRangeStart).ToString("0.0") + " m/z; this is likely a MRM scan, but MASIC doesn't support inferring the " + "MRM transition masses from the observed m/z values.  Results will likely not be meaningful");
                            if (mWarnCount == 5)
                            {
                                ReportMessage("Additional m/z range warnings will not be shown");
                            }
                        }
                    }

                    var mrmMassRange = new udtMRMMassRangeType()
                    {
                        StartMass = spectrumInfo.mzRangeStart,
                        EndMass = spectrumInfo.mzRangeEnd
                    };
                    mrmMassRange.CentralMass = Math.Round(mrmMassRange.StartMass + (mrmMassRange.EndMass - mrmMassRange.StartMass) / 2, 6);
                    mrmScan.MRMInfo.MRMMassList.Add(mrmMassRange);
                }

                scanInfo.MRMScanInfo = clsMRMProcessing.DuplicateMRMInfo(mrmScan.MRMInfo, spectrumInfo.ParentIonMZ);
                if (scanList.SurveyScans.Count == 0)
                {
                    // Need to add a "fake" survey scan that we can map this parent ion to
                    mLastNonZoomSurveyScanIndex = scanList.AddFakeSurveyScan();
                }
            }
            else
            {
                scanInfo.MRMScanInfo.MRMMassCount = 0;
            }

            scanInfo.LowMass = spectrumInfo.mzRangeStart;
            scanInfo.HighMass = spectrumInfo.mzRangeEnd;
            scanInfo.IsFTMS = IsHighResolutionSpectrum(mzXmlSourceSpectrum.FilterLine);
            scanList.FragScans.Add(scanInfo);
            int fragScanIndex = scanList.FragScans.Count - 1;
            scanList.AddMasterScanEntry(clsScanList.eScanTypeConstants.FragScan, fragScanIndex);

            // Note: Even if keepRawSpectra = False, we still need to load the raw data so that we can compute the noise level for the spectrum
            double msDataResolution = mOptions.BinningOptions.BinSize / sicOptions.CompressToleranceDivisorForDa;
            StoreSpectrum(msSpectrum, scanInfo, spectraCache, sicOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions, clsMASIC.DISCARD_LOW_INTENSITY_MSMS_DATA_ON_LOAD, sicOptions.CompressMSMSSpectraData, msDataResolution, mKeepRawSpectra && mKeepMSMSSpectra);
            SaveScanStatEntry(dataOutputHandler.OutputFileHandles.ScanStats, clsScanList.eScanTypeConstants.FragScan, scanInfo, sicOptions.DatasetID);
            if (eMRMScanType == MRMScanTypeConstants.NotMRM)
            {
                // This is not an MRM scan
                mParentIonProcessor.AddUpdateParentIons(scanList, mLastNonZoomSurveyScanIndex, spectrumInfo.ParentIonMZ, fragScanIndex, spectraCache, sicOptions);
            }
            else
            {
                // This is an MRM scan
                mParentIonProcessor.AddUpdateParentIons(scanList, mLastNonZoomSurveyScanIndex, spectrumInfo.ParentIonMZ, scanInfo.MRMScanInfo, spectraCache, sicOptions);
            }

            if (mLastNonZoomSurveyScanIndex >= 0)
            {
                int precursorScanNumber = scanList.SurveyScans[mLastNonZoomSurveyScanIndex].ScanNumber;

                // Compute the interference of the parent ion in the MS1 spectrum for this frag scan
                scanInfo.FragScanInfo.InterferenceScore = ComputeInterference(mzMLSpectrum, scanInfo, precursorScanNumber);
            }

            return true;
        }

        /// <summary>
        /// Get the first filter string defined in the CVParams of this mzML Spectrum
        /// </summary>
        /// <param name="mzMLSpectrum"></param>
        /// <returns></returns>
        private string GetFilterString(SimpleMzMLReader.ParamData mzMLSpectrum)
        {
            var filterStrings = (from item in mzMLSpectrum.CVParams
                                 where item.TermInfo.Cvid == CV.CVID.MS_filter_string
                                 select item).ToList();
            if (filterStrings.Count <= 0)
            {
                return string.Empty;
            }

            var filterString = filterStrings.First().Value;
            return filterString;
        }

        private clsSpectrumInfoMzXML GetSpectrumInfoFromMzMLSpectrum(SimpleMzMLReader.SimpleSpectrum mzMLSpectrum, IReadOnlyList<double> mzList, IReadOnlyList<double> intensityList, bool thermoRawFile)
        {
            var mzXmlSourceSpectrum = new clsSpectrumInfoMzXML()
            {
                SpectrumID = mzMLSpectrum.ScanNumber,
                ScanNumber = mzMLSpectrum.ScanNumber,
                RetentionTimeMin = clsUtilities.CSngSafe(mzMLSpectrum.ScanStartTime),
                MSLevel = mzMLSpectrum.MsLevel,
                TotalIonCurrent = mzMLSpectrum.TotalIonCurrent,
                DataCount = mzList.Count
            };
            if (mzXmlSourceSpectrum.DataCount > 0)
            {
                double basePeakMz = mzList[0];
                double bpi = intensityList[0];
                double mzMin = basePeakMz;
                double mzMax = basePeakMz;
                for (int i = 1; i <= mzXmlSourceSpectrum.DataCount - 1; i++)
                {
                    if (intensityList[i] > bpi)
                    {
                        basePeakMz = mzList[i];
                        bpi = intensityList[i];
                    }

                    if (mzList[i] < mzMin)
                    {
                        mzMin = mzList[i];
                    }
                    else if (mzList[i] > mzMax)
                    {
                        mzMax = mzList[i];
                    }
                }

                mzXmlSourceSpectrum.BasePeakMZ = basePeakMz;
                mzXmlSourceSpectrum.BasePeakIntensity = clsUtilities.CSngSafe(bpi);
                mzXmlSourceSpectrum.mzRangeStart = clsUtilities.CSngSafe(mzMin);
                mzXmlSourceSpectrum.mzRangeEnd = clsUtilities.CSngSafe(mzMax);
            }

            if (mzXmlSourceSpectrum.MSLevel > 1)
            {
                var firstPrecursor = mzMLSpectrum.Precursors[0];
                mzXmlSourceSpectrum.ParentIonMZ = firstPrecursor.IsolationWindow.TargetMz;

                // Verbose activation method description:
                // Dim activationMethod = firstPrecursor.ActivationMethod

                var precursorParams = firstPrecursor.CVParams;
                var activationMethods = new SortedSet<string>();
                var supplementalMethods = new SortedSet<string>();
                foreach (var cvParam in precursorParams)
                {
                    switch (cvParam.TermInfo.Cvid)
                    {
                        case CV.CVID.MS_collision_induced_dissociation:
                        case CV.CVID.MS_low_energy_collision_induced_dissociation:
                        case CV.CVID.MS_in_source_collision_induced_dissociation:
                        case CV.CVID.MS_trap_type_collision_induced_dissociation:
                            activationMethods.Add("CID");
                            break;
                        case CV.CVID.MS_plasma_desorption:
                            activationMethods.Add("PD");
                            break;
                        case CV.CVID.MS_post_source_decay:
                            activationMethods.Add("PSD");
                            break;
                        case CV.CVID.MS_surface_induced_dissociation:
                            activationMethods.Add("SID");
                            break;
                        case CV.CVID.MS_blackbody_infrared_radiative_dissociation:
                            activationMethods.Add("BIRD");
                            break;
                        case CV.CVID.MS_electron_capture_dissociation:
                            activationMethods.Add("ECD");
                            break;
                        case CV.CVID.MS_infrared_multiphoton_dissociation:
                            // ReSharper disable once StringLiteralTypo
                            activationMethods.Add("IRPD");
                            break;
                        case CV.CVID.MS_sustained_off_resonance_irradiation:
                            activationMethods.Add("ORI");
                            break;
                        case CV.CVID.MS_beam_type_collision_induced_dissociation:
                            activationMethods.Add("HCD");
                            break;
                        case CV.CVID.MS_photodissociation:
                            // ReSharper disable once StringLiteralTypo
                            activationMethods.Add("UVPD");
                            break;
                        case CV.CVID.MS_electron_transfer_dissociation:
                            activationMethods.Add("ETD");
                            break;
                        case CV.CVID.MS_pulsed_q_dissociation:
                            activationMethods.Add("PQD");
                            break;
                        case CV.CVID.MS_LIFT:
                            activationMethods.Add("LIFT");
                            break;
                        case CV.CVID.MS_Electron_Transfer_Higher_Energy_Collision_Dissociation__EThcD_:
                            activationMethods.Add("EThcD");
                            break;
                        case CV.CVID.MS_supplemental_beam_type_collision_induced_dissociation:
                            supplementalMethods.Add("HCD");
                            break;
                        case CV.CVID.MS_supplemental_collision_induced_dissociation:
                            supplementalMethods.Add("CID");
                            break;
                    }
                }

                if (activationMethods.Contains("ETD"))
                {
                    if (supplementalMethods.Contains("CID"))
                    {
                        activationMethods.Remove("ETD");
                        activationMethods.Add("ETciD");
                    }
                    else if (supplementalMethods.Contains("HCD"))
                    {
                        activationMethods.Remove("ETD");
                        activationMethods.Add("EThcD");
                    }
                }

                mzXmlSourceSpectrum.ActivationMethod = string.Join(Conversions.ToString(','), activationMethods);
            }

            // Store the "filter string" in .FilterLine

            string filterString = GetFilterString(mzMLSpectrum);
            if (!string.IsNullOrWhiteSpace(filterString))
            {
                if (thermoRawFile)
                {
                    mzXmlSourceSpectrum.FilterLine = XRawFileIO.MakeGenericThermoScanFilter(filterString);
                    mzXmlSourceSpectrum.ScanType = XRawFileIO.GetScanTypeNameFromThermoScanFilterText(filterString);
                }
                else
                {
                    mzXmlSourceSpectrum.FilterLine = filterString;
                }
            }

            if (string.IsNullOrWhiteSpace(filterString) || !thermoRawFile)
            {
                var matchingParams = mzMLSpectrum.GetCVParamsChildOf(CV.CVID.MS_spectrum_type);
                if (matchingParams.Count > 0)
                {
                    mzXmlSourceSpectrum.ScanType = matchingParams.First().TermInfo.Name;
                }
            }

            var centroidParams = (from item in mzMLSpectrum.CVParams
                                  where item.TermInfo.Cvid == CV.CVID.MS_centroid_spectrum
                                  select item).ToList();
            mzXmlSourceSpectrum.Centroided = centroidParams.Count > 0;
            return mzXmlSourceSpectrum;
        }

        private void InitOptions(clsScanList scanList, bool keepRawSpectra, bool keepMSMSSpectra)
        {
            scanList.Initialize();
            InitBaseOptions(scanList, keepRawSpectra, keepMSMSSpectra);
            mLastSurveyScanIndexInMasterSeqOrder = -1;
            mMostRecentPrecursorScan = -1;
            mWarnCount = 0;
        }

        private bool IsHighResolutionSpectrum(string filterString)
        {
            if (filterString.IndexOf("FTMS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private bool IsSrmChromatogram(SimpleMzMLReader.ParamData chromatogramItem)
        {
            if (chromatogramItem.CVParams.Count > 0)
            {
                foreach (var item in chromatogramItem.CVParams)
                {
                    switch (item.TermInfo.Cvid)
                    {
                        case CV.CVID.MS_total_ion_current_chromatogram:
                            // Skip this chromatogram
                            return false;
                        case CV.CVID.MS_selected_ion_current_chromatogram:
                        case CV.CVID.MS_selected_ion_monitoring_chromatogram:
                        case CV.CVID.MS_selected_reaction_monitoring_chromatogram:
                            return true;
                    }
                }
            }

            return false;
        }

        private bool StoreSimulatedDataPoint(IDictionary<int, Dictionary<double, double>> simulatedSpectraByScan, IDictionary<int, double> simulatedSpectraTimes, int scanToStore, double elutionTime, double mz, double intensity)
        {
            // Keys in this dictionary are m/z; values are intensity for the m/z
            Dictionary<double, double> mzListForScan = null;
            if (!simulatedSpectraByScan.TryGetValue(scanToStore, out mzListForScan))
            {
                mzListForScan = new Dictionary<double, double>();
                simulatedSpectraByScan.Add(scanToStore, mzListForScan);
                simulatedSpectraTimes.Add(scanToStore, elutionTime);
            }

            double existingIntensity;
            if (mzListForScan.TryGetValue(mz, out existingIntensity))
            {
                return false;
            }
            else
            {
                mzListForScan.Add(mz, intensity);
                return true;
            }
        }

        private void StoreSpectrum(clsMSSpectrum msSpectrum, clsScanInfo scanInfo, clsSpectraCache spectraCache, clsBaselineNoiseOptions noiseThresholdOptions, bool discardLowIntensityData, bool compressSpectraData, double msDataResolution, bool keepRawSpectrum)
        {
            try
            {
                if (msSpectrum.IonsMZ == null || msSpectrum.IonsIntensity == null)
                {
                    scanInfo.IonCount = 0;
                    scanInfo.IonCountRaw = 0;
                }
                else
                {
                    scanInfo.IonCount = msSpectrum.IonCount;
                    scanInfo.IonCountRaw = scanInfo.IonCount;
                }

                if (msSpectrum.ScanNumber != scanInfo.ScanNumber)
                {
                    msSpectrum.ScanNumber = scanInfo.ScanNumber;
                }

                if (scanInfo.IonCount > 0)
                {
                    // Confirm the total scan intensity stored in the mzXML file
                    double totalIonIntensity = 0;
                    for (int ionIndex = 0; ionIndex <= msSpectrum.IonCount - 1; ionIndex++)
                        totalIonIntensity += msSpectrum.IonsIntensity[ionIndex];
                    if (scanInfo.TotalIonIntensity < float.Epsilon)
                    {
                        scanInfo.TotalIonIntensity = totalIonIntensity;
                    }

                    mScanTracking.ProcessAndStoreSpectrum(scanInfo, this, spectraCache, msSpectrum, noiseThresholdOptions, discardLowIntensityData, compressSpectraData, msDataResolution, keepRawSpectrum);
                }
                else
                {
                    scanInfo.TotalIonIntensity = 0;
                }
            }
            catch (Exception ex)
            {
                ReportError("Error in clsMasic->StoreSpectrum ", ex);
            }
        }

        private void UpdateCachedPrecursorScanData(int scanNumber, clsMSSpectrum msSpectrum)
        {
            mMostRecentPrecursorScan = scanNumber;
            mCentroidedPrecursorIonsMz.Clear();
            mCentroidedPrecursorIonsIntensity.Clear();
            for (int i = 0; i <= msSpectrum.IonCount - 1; i++)
            {
                mCentroidedPrecursorIonsMz.Add(msSpectrum.IonsMZ[i]);
                mCentroidedPrecursorIonsIntensity.Add(msSpectrum.IonsIntensity[i]);
            }
        }

        [CLSCompliant(false)]
        protected bool UpdateDatasetFileStats(FileInfo rawFileInfo, int datasetID, SimpleMzMLReader xmlReader)
        {
            // Read the file info from the file system
            bool success = UpdateDatasetFileStats(rawFileInfo, datasetID);
            if (!success)
                return false;
            if (xmlReader.StartTimeStamp > DateTime.MinValue)
            {
                mDatasetFileInfo.AcqTimeStart = xmlReader.StartTimeStamp;
                mDatasetFileInfo.AcqTimeEnd = mDatasetFileInfo.AcqTimeStart;
            }

            // Note that .ScanCount and AcqTimeEnd will be updated by ExtractScanInfoFromMzMLDataFile

            return true;
        }

        private void UpdateMSXmlScanType(clsScanInfo scanInfo, int msLevel, string defaultScanType, bool isMzXML, clsSpectrumInfoMzXML mzXmlSourceSpectrum)
        {
            if (!isMzXML)
            {
                // Not a .mzXML file
                // Use the defaults
                if (string.IsNullOrWhiteSpace(scanInfo.ScanHeaderText))
                {
                    if (mzXmlSourceSpectrum == null || string.IsNullOrWhiteSpace(mzXmlSourceSpectrum.FilterLine))
                    {
                        scanInfo.ScanHeaderText = string.Empty;
                    }
                    else
                    {
                        scanInfo.ScanHeaderText = mzXmlSourceSpectrum.FilterLine;
                    }
                }

                scanInfo.ScanTypeName = defaultScanType;
                return;
            }

            // Store the filter line text in .ScanHeaderText
            // Only Thermo files processed with ReadW will have a FilterLine
            scanInfo.ScanHeaderText = mzXmlSourceSpectrum.FilterLine;
            if (!string.IsNullOrEmpty(scanInfo.ScanHeaderText))
            {
                // This is a Thermo file; auto define .ScanTypeName using the FilterLine text
                scanInfo.ScanTypeName = XRawFileIO.GetScanTypeNameFromThermoScanFilterText(scanInfo.ScanHeaderText);

                // Now populate .SIMScan, .MRMScanType and .ZoomScan
                int msLevelFromFilter;
                bool simScan;
                MRMScanTypeConstants mrmScanType;
                bool zoomScan;
                XRawFileIO.ValidateMSScan(scanInfo.ScanHeaderText, out msLevelFromFilter, out simScan, out mrmScanType, out zoomScan);
                scanInfo.SIMScan = simScan;
                scanInfo.MRMScanType = mrmScanType;
                scanInfo.ZoomScan = zoomScan;
                return;
            }

            scanInfo.ScanHeaderText = string.Empty;
            scanInfo.ScanTypeName = mzXmlSourceSpectrum.ScanType;
            if (string.IsNullOrEmpty(scanInfo.ScanTypeName))
            {
                scanInfo.ScanTypeName = defaultScanType;
            }
            else
            {
                // Possibly update .ScanTypeName to match the values returned by XRawFileIO.GetScanTypeNameFromThermoScanFilterText()
                switch (scanInfo.ScanTypeName.ToLower())
                {
                    case var @case when @case == clsSpectrumInfoMzXML.ScanTypeNames.Full.ToLower():
                        if (msLevel <= 1)
                        {
                            scanInfo.ScanTypeName = "MS";
                        }
                        else
                        {
                            scanInfo.ScanTypeName = "MSn";
                        }

                        break;

                    case var case1 when case1 == clsSpectrumInfoMzXML.ScanTypeNames.zoom.ToLower():
                        scanInfo.ScanTypeName = "Zoom-MS";
                        break;
                    case var case2 when case2 == clsSpectrumInfoMzXML.ScanTypeNames.MRM.ToLower():
                        scanInfo.ScanTypeName = "MRM";
                        scanInfo.MRMScanType = MRMScanTypeConstants.SRM;
                        break;
                    case var case3 when case3 == clsSpectrumInfoMzXML.ScanTypeNames.SRM.ToLower():
                        scanInfo.ScanTypeName = "CID-SRM";
                        scanInfo.MRMScanType = MRMScanTypeConstants.SRM;
                        break;
                    default:
                        // Leave .ScanTypeName unchanged
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(mzXmlSourceSpectrum.ActivationMethod))
            {
                // Update ScanTypeName to include the activation method,
                // For example, to be CID-MSn instead of simply MSn
                scanInfo.ScanTypeName = mzXmlSourceSpectrum.ActivationMethod + "-" + scanInfo.ScanTypeName;
                if ((scanInfo.ScanTypeName ?? "") == "HCD-MSn")
                {
                    // HCD spectra are always high res; auto-update things
                    scanInfo.ScanTypeName = "HCD-HMSn";
                }
            }
        }
    }
}
