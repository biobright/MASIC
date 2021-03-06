﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MASICPeakFinder;
using PRISM;

namespace MASIC
{
    public class clsCustomSICList : EventNotifier
    {
        #region "Constants and Enums"

        public const string CUSTOM_SIC_TYPE_ABSOLUTE = "Absolute";
        public const string CUSTOM_SIC_TYPE_RELATIVE = "Relative";
        public const string CUSTOM_SIC_TYPE_ACQUISITION_TIME = "AcquisitionTime";

        public enum eCustomSICScanTypeConstants
        {
            /// <summary>
            /// Absolute scan number
            /// </summary>
            Absolute = 0,

            /// <summary>
            /// Relative scan number (ranging from 0 to 1, where 0 is the first scan and 1 is the last scan)
            /// </summary>
            Relative = 1,

            /// <summary>
            /// The scan's acquisition time (aka elution time if using liquid chromatography)
            /// </summary>
            AcquisitionTime = 2,

            /// <summary>
            /// Undefined
            /// </summary>
            Undefined = 3
        }

        #endregion

        #region "Properties"

        public string CustomSICListFileName
        {
            get
            {
                if (mCustomSICListFileName == null)
                    return string.Empty;

                return mCustomSICListFileName;
            }
            set
            {
                if (value == null)
                {
                    mCustomSICListFileName = string.Empty;
                }
                else
                {
                    mCustomSICListFileName = value.Trim();
                }
            }
        }

        public eCustomSICScanTypeConstants ScanToleranceType { get; set; }

        /// <summary>
        /// This is an Integer if ScanToleranceType = eCustomSICScanTypeConstants.Absolute
        /// It is a Single if ScanToleranceType = .Relative or ScanToleranceType = .AcquisitionTime
        /// Set to 0 to search the entire file for the given mass
        /// </summary>
        public float ScanOrAcqTimeTolerance { get; set; }

        /// <summary>
        /// List of m/z values to search for
        /// </summary>
        public List<clsCustomMZSearchSpec> CustomMZSearchValues { get; }

        /// <summary>
        /// When True, then will only search for the m/z values listed in the custom m/z list
        /// </summary>
        public bool LimitSearchToCustomMZList { get; set; }

        public string RawTextMZList { get; set; }
        public string RawTextMZToleranceDaList { get; set; }
        public string RawTextScanOrAcqTimeCenterList { get; set; }
        public string RawTextScanOrAcqTimeToleranceList { get; set; }

        #endregion

        #region "Classwide variables"

        private string mCustomSICListFileName;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public clsCustomSICList()
        {
            CustomMZSearchValues = new List<clsCustomMZSearchSpec>();
        }

        /// <summary>
        /// Store custom SIC values defined in CustomMZSearchValues
        /// </summary>
        /// <param name="scanList"></param>
        /// <param name="defaultSICTolerance"></param>
        /// <param name="sicToleranceIsPPM"></param>
        /// <param name="defaultScanOrAcqTimeTolerance"></param>
        public void AddCustomSICValues(
            clsScanList scanList,
            double defaultSICTolerance,
            bool sicToleranceIsPPM,
            float defaultScanOrAcqTimeTolerance)
        {
            var scanOrAcqTimeSumCount = 0;
            float scanOrAcqTimeSumForAveraging = 0;

            try
            {
                if (CustomMZSearchValues.Count == 0)
                {
                    return;
                }

                var scanNumScanConverter = new clsScanNumScanTimeConversion();
                RegisterEvents(scanNumScanConverter);

                foreach (var customMzSearchValue in CustomMZSearchValues)
                {
                    // Add a new parent ion entry to .ParentIons() for this custom MZ value
                    var currentParentIon = new clsParentIonInfo(customMzSearchValue.MZ);

                    if (customMzSearchValue.ScanOrAcqTimeCenter < float.Epsilon)
                    {
                        // Set the SurveyScanIndex to the center of the analysis
                        currentParentIon.SurveyScanIndex = scanNumScanConverter.FindNearestSurveyScanIndex(
                            scanList, 0.5F, eCustomSICScanTypeConstants.Relative);
                    }
                    else
                    {
                        currentParentIon.SurveyScanIndex = scanNumScanConverter.FindNearestSurveyScanIndex(
                            scanList, customMzSearchValue.ScanOrAcqTimeCenter, ScanToleranceType);
                    }

                    // Find the next MS2 scan that occurs after the survey scan (parent scan)
                    var surveyScanNumberAbsolute = 0;
                    if (currentParentIon.SurveyScanIndex < scanList.SurveyScans.Count)
                    {
                        surveyScanNumberAbsolute = scanList.SurveyScans[currentParentIon.SurveyScanIndex].ScanNumber + 1;
                    }

                    if (scanList.MasterScanOrderCount == 0)
                    {
                        currentParentIon.FragScanIndices.Add(0);
                    }
                    else
                    {
                        var fragScanIndexMatch = clsBinarySearch.BinarySearchFindNearest(
                            scanList.MasterScanNumList,
                            surveyScanNumberAbsolute,
                            clsBinarySearch.eMissingDataModeConstants.ReturnClosestPoint);

                        while (fragScanIndexMatch < scanList.MasterScanOrderCount && scanList.MasterScanOrder[fragScanIndexMatch].ScanType == clsScanList.eScanTypeConstants.SurveyScan)
                            fragScanIndexMatch += 1;

                        if (fragScanIndexMatch == scanList.MasterScanOrderCount)
                        {
                            // Did not find the next frag scan; find the previous frag scan
                            fragScanIndexMatch -= 1;
                            while (fragScanIndexMatch > 0 && scanList.MasterScanOrder[fragScanIndexMatch].ScanType == clsScanList.eScanTypeConstants.SurveyScan)
                                fragScanIndexMatch -= 1;

                            if (fragScanIndexMatch < 0)
                                fragScanIndexMatch = 0;
                        }

                        // This is a custom SIC-based parent ion
                        // Prior to August 2014, we set .FragScanIndices(0) = 0, which made it appear that the fragmentation scan was the first MS2 spectrum in the dataset for all custom SICs
                        // This caused undesirable display results in MASIC browser, so we now set it to the next MS2 scan that occurs after the survey scan (parent scan)
                        if (scanList.MasterScanOrder[fragScanIndexMatch].ScanType == clsScanList.eScanTypeConstants.FragScan)
                        {
                            currentParentIon.FragScanIndices.Add(scanList.MasterScanOrder[fragScanIndexMatch].ScanIndexPointer);
                        }
                        else
                        {
                            currentParentIon.FragScanIndices.Add(0);
                        }
                    }

                    currentParentIon.CustomSICPeak = true;
                    currentParentIon.CustomSICPeakComment = customMzSearchValue.Comment;
                    currentParentIon.CustomSICPeakMZToleranceDa = customMzSearchValue.MZToleranceDa;
                    currentParentIon.CustomSICPeakScanOrAcqTimeTolerance = customMzSearchValue.ScanOrAcqTimeTolerance;

                    if (currentParentIon.CustomSICPeakMZToleranceDa < double.Epsilon)
                    {
                        if (sicToleranceIsPPM)
                        {
                            currentParentIon.CustomSICPeakMZToleranceDa = clsUtilities.PPMToMass(defaultSICTolerance, currentParentIon.MZ);
                        }
                        else
                        {
                            currentParentIon.CustomSICPeakMZToleranceDa = defaultSICTolerance;
                        }
                    }

                    if (currentParentIon.CustomSICPeakScanOrAcqTimeTolerance < float.Epsilon)
                    {
                        currentParentIon.CustomSICPeakScanOrAcqTimeTolerance = defaultScanOrAcqTimeTolerance;
                    }
                    else
                    {
                        scanOrAcqTimeSumForAveraging += currentParentIon.CustomSICPeakScanOrAcqTimeTolerance;
                        scanOrAcqTimeSumCount += 1;
                    }

                    if (currentParentIon.SurveyScanIndex < scanList.SurveyScans.Count)
                    {
                        currentParentIon.OptimalPeakApexScanNumber =
                            scanList.SurveyScans[currentParentIon.SurveyScanIndex].ScanNumber;
                    }
                    else
                    {
                        currentParentIon.OptimalPeakApexScanNumber = 1;
                    }

                    currentParentIon.PeakApexOverrideParentIonIndex = -1;

                    scanList.ParentIons.Add(currentParentIon);
                }

                if (scanOrAcqTimeSumCount == CustomMZSearchValues.Count && scanOrAcqTimeSumForAveraging > 0)
                {
                    // All of the entries had a custom scan or acq time tolerance defined
                    // Update mScanOrAcqTimeTolerance to the average of the values
                    ScanOrAcqTimeTolerance = (float)(Math.Round(scanOrAcqTimeSumForAveraging / scanOrAcqTimeSumCount, 4));
                }
            }
            catch (Exception ex)
            {
                OnErrorEvent("Error in AddCustomSICValues", ex);
            }
        }

        /// <summary>
        /// Append a custom m/z value to CustomMZSearchValues
        /// </summary>
        /// <param name="mzSearchSpec"></param>
        public void AddMzSearchTarget(clsCustomMZSearchSpec mzSearchSpec)
        {
            if (CustomMZSearchValues.Count > 0)
            {
                RawTextMZList += ",";
                RawTextMZToleranceDaList += ",";
                RawTextScanOrAcqTimeCenterList += ",";
                RawTextScanOrAcqTimeToleranceList += ",";
            }

            RawTextMZList += mzSearchSpec.MZ.ToString(CultureInfo.InvariantCulture);
            RawTextMZToleranceDaList += mzSearchSpec.MZToleranceDa.ToString(CultureInfo.InvariantCulture);
            RawTextScanOrAcqTimeCenterList += mzSearchSpec.ScanOrAcqTimeCenter.ToString(CultureInfo.InvariantCulture);
            RawTextScanOrAcqTimeToleranceList += mzSearchSpec.ScanOrAcqTimeTolerance.ToString(CultureInfo.InvariantCulture);

            CustomMZSearchValues.Add(mzSearchSpec);
        }

        /// <summary>
        /// Parse parallel lists of custom m/z info
        /// </summary>
        /// <param name="mzList">Comma or tab separated list of m/z values</param>
        /// <param name="mzToleranceDaList">Comma or tab separated list of tolerances (in Da)</param>
        /// <param name="scanCenterList">Comma or tab separated list of scan centers</param>
        /// <param name="scanToleranceList">Comma or tab separated list of scan tolerances</param>
        /// <param name="scanCommentList">Comma or tab separated list of comments</param>
        /// <returns></returns>
        public bool ParseCustomSICList(
            string mzList,
            string mzToleranceDaList,
            string scanCenterList,
            string scanToleranceList,
            string scanCommentList)
        {
            var delimiters = new[] { ',', '\t' };

            // Trim any trailing tab characters
            mzList = mzList.TrimEnd('\t');
            mzToleranceDaList = mzToleranceDaList.TrimEnd('\t');
            scanCenterList = scanCenterList.TrimEnd('\t');
            scanToleranceList = scanToleranceList.TrimEnd('\t');
            scanCommentList = scanCommentList.TrimEnd(delimiters);

            var mzValues = mzList.Split(delimiters).ToList();
            var mzToleranceDa = mzToleranceDaList.Split(delimiters).ToList();
            var scanCenters = scanCenterList.Split(delimiters).ToList();
            var scanTolerances = scanToleranceList.Split(delimiters).ToList();
            List<string> lstScanComments;

            if (scanCommentList.Length > 0)
            {
                lstScanComments = scanCommentList.Split(delimiters).ToList();
            }
            else
            {
                lstScanComments = new List<string>();
            }

            ResetMzSearchValues();

            if (mzValues.Count <= 0)
            {
                // Nothing to parse; return true
                return true;
            }

            for (var index = 0; index < mzValues.Count; index++)
            {
                if (!double.TryParse(mzValues[index], out var targetMz))
                {
                    continue;
                }

                var mzSearchSpec = new clsCustomMZSearchSpec(targetMz)
                {
                    MZToleranceDa = 0,
                    ScanOrAcqTimeCenter = 0,                 // Set to 0 to indicate that the entire file should be searched
                    ScanOrAcqTimeTolerance = 0
                };

                if (scanCenters.Count > index)
                {
                    if (clsUtilities.IsNumber(scanCenters[index]))
                    {
                        if (ScanToleranceType == eCustomSICScanTypeConstants.Absolute)
                        {
                            mzSearchSpec.ScanOrAcqTimeCenter = int.Parse(scanCenters[index]);
                        }
                        else
                        {
                            // Includes .Relative and .AcquisitionTime
                            mzSearchSpec.ScanOrAcqTimeCenter = float.Parse(scanCenters[index]);
                        }
                    }
                }

                if (scanTolerances.Count > index)
                {
                    if (clsUtilities.IsNumber(scanTolerances[index]))
                    {
                        if (ScanToleranceType == eCustomSICScanTypeConstants.Absolute)
                        {
                            mzSearchSpec.ScanOrAcqTimeTolerance = int.Parse(scanTolerances[index]);
                        }
                        else
                        {
                            // Includes .Relative and .AcquisitionTime
                            mzSearchSpec.ScanOrAcqTimeTolerance = float.Parse(scanTolerances[index]);
                        }
                    }
                }

                if (mzToleranceDa.Count > index)
                {
                    if (clsUtilities.IsNumber(mzToleranceDa[index]))
                    {
                        mzSearchSpec.MZToleranceDa = double.Parse(mzToleranceDa[index]);
                    }
                }

                if (lstScanComments.Count > index)
                {
                    mzSearchSpec.Comment = lstScanComments[index];
                }
                else
                {
                    mzSearchSpec.Comment = string.Empty;
                }

                AddMzSearchTarget(mzSearchSpec);
            }

            return true;
        }

        /// <summary>
        /// Clear custom m/z targets and reset the default scan tolerance
        /// </summary>
        public void Reset()
        {
            ScanToleranceType = eCustomSICScanTypeConstants.Absolute;
            ScanOrAcqTimeTolerance = 1000;

            ResetMzSearchValues();
        }

        /// <summary>
        /// Clear custom m/z targets
        /// </summary>
        public void ResetMzSearchValues()
        {
            CustomMZSearchValues.Clear();

            RawTextMZList = string.Empty;
            RawTextMZToleranceDaList = string.Empty;
            RawTextScanOrAcqTimeCenterList = string.Empty;
            RawTextScanOrAcqTimeToleranceList = string.Empty;
        }

        /// <summary>
        /// Define custom SIC list values
        /// </summary>
        /// <param name="eScanType"></param>
        /// <param name="mzToleranceDa"></param>
        /// <param name="scanOrAcqTimeToleranceValue"></param>
        /// <param name="mzList"></param>
        /// <param name="mzToleranceList"></param>
        /// <param name="scanOrAcqTimeCenterList"></param>
        /// <param name="scanOrAcqTimeToleranceList"></param>
        /// <param name="scanComments"></param>
        /// <returns>True if success, otherwise false</returns>
        [Obsolete("Use SetCustomSICListValues that takes List(Of clsCustomMZSearchSpec)")]
        public bool SetCustomSICListValues(
            eCustomSICScanTypeConstants eScanType,
            double mzToleranceDa,
            float scanOrAcqTimeToleranceValue,
            double[] mzList,
            double[] mzToleranceList,
            float[] scanOrAcqTimeCenterList,
            float[] scanOrAcqTimeToleranceList,
            string[] scanComments)
        {

            if (mzToleranceList.Length > 0 && mzToleranceList.Length != mzList.Length)
            {
                // Invalid Custom SIC comment list; number of entries doesn't match
                return false;
            }

            if (scanOrAcqTimeCenterList.Length > 0 && scanOrAcqTimeCenterList.Length != mzList.Length)
            {
                // Invalid Custom SIC scan center list; number of entries doesn't match
                return false;
            }

            if (scanOrAcqTimeToleranceList.Length > 0 && scanOrAcqTimeToleranceList.Length != mzList.Length)
            {
                // Invalid Custom SIC scan center list; number of entries doesn't match
                return false;
            }

            if (scanComments.Length > 0 && scanComments.Length != mzList.Length)
            {
                // Invalid Custom SIC comment list; number of entries doesn't match
                return false;
            }

            ResetMzSearchValues();

            ScanToleranceType = eScanType;

            // This value is used if scanOrAcqTimeToleranceList is blank or for any entries in scanOrAcqTimeToleranceList() that are zero
            ScanOrAcqTimeTolerance = scanOrAcqTimeToleranceValue;

            if (mzList.Length == 0)
            {
                return true;
            }

            for (var index = 0; index < mzList.Length; index++)
            {
                var mzSearchSpec = new clsCustomMZSearchSpec(mzList[index]);

                if (mzToleranceList.Length > index && mzToleranceList[index] > 0)
                {
                    mzSearchSpec.MZToleranceDa = mzToleranceList[index];
                }
                else
                {
                    mzSearchSpec.MZToleranceDa = mzToleranceDa;
                }

                if (scanOrAcqTimeCenterList.Length > index)
                {
                    mzSearchSpec.ScanOrAcqTimeCenter = scanOrAcqTimeCenterList[index];
                }
                else
                {
                    mzSearchSpec.ScanOrAcqTimeCenter = 0;
                }         // Set to 0 to indicate that the entire file should be searched

                if (scanOrAcqTimeToleranceList.Length > index && scanOrAcqTimeToleranceList[index] > 0)
                {
                    mzSearchSpec.ScanOrAcqTimeTolerance = scanOrAcqTimeToleranceList[index];
                }
                else
                {
                    mzSearchSpec.ScanOrAcqTimeTolerance = scanOrAcqTimeToleranceValue;
                }

                if (scanComments.Length > 0 && scanComments.Length > index)
                {
                    mzSearchSpec.Comment = scanComments[index];
                }
                else
                {
                    mzSearchSpec.Comment = string.Empty;
                }

                AddMzSearchTarget(mzSearchSpec);
            }

            ValidateCustomSICList();
            return true;
        }

        /// <summary>
        /// Define custom SIC list values
        /// </summary>
        /// <param name="eScanType"></param>
        /// <param name="scanOrAcqTimeToleranceValue"></param>
        /// <param name="mzSearchSpecs"></param>
        /// <returns>True if success, false if error</returns>
        public bool SetCustomSICListValues(
            eCustomSICScanTypeConstants eScanType,
            float scanOrAcqTimeToleranceValue,
            List<clsCustomMZSearchSpec> mzSearchSpecs)
        {

            ResetMzSearchValues();

            ScanToleranceType = eScanType;

            // This value is used if scanOrAcqTimeToleranceList is blank or for any entries in scanOrAcqTimeToleranceList() that are zero
            ScanOrAcqTimeTolerance = scanOrAcqTimeToleranceValue;

            if (mzSearchSpecs.Count == 0)
            {
                return true;
            }

            foreach (var mzSearchSpec in mzSearchSpecs)
                AddMzSearchTarget(mzSearchSpec);

            ValidateCustomSICList();

            return true;
        }

        public void ValidateCustomSICList()
        {
            if (CustomMZSearchValues == null ||
                CustomMZSearchValues.Count == 0)
            {
                return;
            }

            // Check whether all of the values are between 0 and 1
            // If they are, then auto-switch .ScanToleranceType to "Relative"

            var countBetweenZeroAndOne = 0;
            var countOverOne = 0;

            foreach (var customMzValue in CustomMZSearchValues)
            {
                if (customMzValue.ScanOrAcqTimeCenter > 1)
                {
                    countOverOne += 1;
                }
                else
                {
                    countBetweenZeroAndOne += 1;
                }
            }

            if (countOverOne == 0 && countBetweenZeroAndOne > 0)
            {
                if (ScanToleranceType == eCustomSICScanTypeConstants.Absolute)
                {
                    // No values were greater than 1 but at least one value is between 0 and 1
                    // Change the ScanToleranceType mode from Absolute to Relative
                    ScanToleranceType = eCustomSICScanTypeConstants.Relative;
                }
            }

            if (countOverOne > 0 && countBetweenZeroAndOne == 0)
            {
                if (ScanToleranceType == eCustomSICScanTypeConstants.Relative)
                {
                    // The ScanOrAcqTimeCenter values cannot be relative
                    // Change the ScanToleranceType mode from Relative to Absolute
                    ScanToleranceType = eCustomSICScanTypeConstants.Absolute;
                }
            }
        }

        public override string ToString()
        {
            if (CustomMZSearchValues == null || CustomMZSearchValues.Count == 0)
            {
                return "0 custom m/z search values";
            }

            return CustomMZSearchValues.Count + " custom m/z search values";
        }
    }
}
