﻿Imports MASIC.clsMASIC
Imports MASIC.DataOutput
Imports MSDataFileReader
Imports ThermoRawFileReader

Namespace DataInput

    Public Class clsDataImportMSXml
        Inherits clsDataImport

        Public Sub New(
          masicOptions As clsMASICOptions,
          peakFinder As MASICPeakFinder.clsMASICPeakFinder,
          parentIonProcessor As clsParentIonProcessing,
          scanTracking As clsScanTracking)
            MyBase.New(masicOptions, peakFinder, parentIonProcessor, scanTracking)
        End Sub

        Public Function ExtractScanInfoFromMZXMLDataFile(
          filePath As String,
          scanList As clsScanList,
          objSpectraCache As clsSpectraCache,
          dataOutputHandler As clsDataOutput,
          keepRawSpectra As Boolean,
          keepMSMSSpectra As Boolean) As Boolean

            Dim objXMLReader As clsMSDataFileReaderBaseClass

            Try
                objXMLReader = New clsMzXMLFileReader
                Return ExtractScanInfoFromMSXMLDataFile(filePath, objXMLReader, scanList, objSpectraCache,
                                                        dataOutputHandler, keepRawSpectra, keepMSMSSpectra)

            Catch ex As Exception
                ReportError("Error in ExtractScanInfoFromMZXMLDataFile", ex, eMasicErrorCodes.InputFileDataReadError)
                Return False
            End Try

        End Function

        Public Function ExtractScanInfoFromMZDataFile(
          filePath As String,
          scanList As clsScanList,
          objSpectraCache As clsSpectraCache,
          dataOutputHandler As clsDataOutput,
          keepRawSpectra As Boolean,
          keepMSMSSpectra As Boolean) As Boolean

            Dim objXMLReader As clsMSDataFileReaderBaseClass

            Try
                objXMLReader = New clsMzDataFileReader
                Return ExtractScanInfoFromMSXMLDataFile(filePath, objXMLReader, scanList, objSpectraCache,
                                                        dataOutputHandler,
                                                        keepRawSpectra, keepMSMSSpectra)

            Catch ex As Exception
                ReportError("Error in ExtractScanInfoFromMZDataFile", ex, eMasicErrorCodes.InputFileDataReadError)
                Return False
            End Try

        End Function

        Private Function ExtractScanInfoFromMSXMLDataFile(
          filePath As String,
          objXMLReader As clsMSDataFileReaderBaseClass,
          scanList As clsScanList,
          objSpectraCache As clsSpectraCache,
          dataOutputHandler As clsDataOutput,
          keepRawSpectra As Boolean,
          keepMSMSSpectra As Boolean) As Boolean

            ' Returns True if Success, False if failure
            ' Note: This function assumes filePath exists

            Dim warnCount = 0
            Dim success As Boolean

            Try
                Console.Write("Reading MSXml data file ")
                ReportMessage("Reading MSXml data file")

                UpdateProgress(0, "Opening data file:" & ControlChars.NewLine & Path.GetFileName(filePath))

                ' Obtain the full path to the file
                Dim msXmlFileInfo = New FileInfo(filePath)
                Dim inputFileFullPath = msXmlFileInfo.FullName

                Dim datasetID = mOptions.SICOptions.DatasetNumber
                Dim sicOptions = mOptions.SICOptions

                success = UpdateDatasetFileStats(msXmlFileInfo, datasetID)
                mDatasetFileInfo.ScanCount = 0

                ' Open a handle to the data file
                If Not objXMLReader.OpenFile(inputFileFullPath) Then
                    ReportError("Error opening input data file: " & inputFileFullPath)
                    SetLocalErrorCode(eMasicErrorCodes.InputFileAccessError)
                    Return False
                End If

                ' We won't know the total scan count until we have read all the data
                ' Thus, initially reserve space for 1000 scans

                scanList.Initialize(1000, 1000)
                Dim lastSurveyScanIndex = -1
                Dim lastSurveyScanIndexInMasterSeqOrder = -1
                Dim lastNonZoomSurveyScanIndex = -1

                Dim scanFound As Boolean
                Dim scansOutOfRange = 0

                scanList.SIMDataPresent = False
                scanList.MRMDataPresent = False

                UpdateProgress("Reading XML data" & ControlChars.NewLine & Path.GetFileName(filePath))
                ReportMessage("Reading XML data from " & filePath)

                Do
                    Dim objSpectrumInfo As clsSpectrumInfo = Nothing
                    scanFound = objXMLReader.ReadNextSpectrum(objSpectrumInfo)

                    If Not scanFound Then Continue Do

                    mDatasetFileInfo.ScanCount += 1

                    Dim objMSSpectrum = New clsMSSpectrum()

                    With objMSSpectrum
                        .IonCount = objSpectrumInfo.DataCount

                        ReDim .IonsMZ(.IonCount - 1)
                        ReDim .IonsIntensity(.IonCount - 1)

                        objSpectrumInfo.MZList.CopyTo(.IonsMZ, 0)
                        objSpectrumInfo.IntensityList.CopyTo(.IonsIntensity, 0)
                    End With

                    ' No Error
                    If mScanTracking.CheckScanInRange(objSpectrumInfo.ScanNumber, objSpectrumInfo.RetentionTimeMin, mOptions.SICOptions) Then
                        ExtractScanInfoWork(scanList, objSpectraCache, dataOutputHandler,
                                            sicOptions, objMSSpectrum, objSpectrumInfo,
                                            lastSurveyScanIndex,
                                            lastSurveyScanIndexInMasterSeqOrder,
                                            lastNonZoomSurveyScanIndex,
                                            warnCount,
                                            keepRawSpectra,
                                            keepMSMSSpectra)
                    Else
                        scansOutOfRange += 1
                    End If

                    UpdateProgress(CShort(Math.Round(objXMLReader.ProgressPercentComplete, 0)))

                    UpdateCacheStats(objSpectraCache)

                    If mOptions.AbortProcessing Then
                        scanList.ProcessingIncomplete = True
                        Exit Do
                    End If

                    If (scanList.MasterScanOrderCount - 1) Mod 100 = 0 Then
                        ReportMessage("Reading scan index: " & (scanList.MasterScanOrderCount - 1).ToString())
                        Console.Write(".")
                    End If


                Loop While scanFound

                ' Shrink the memory usage of the scanList arrays
                ReDim Preserve scanList.MasterScanOrder(scanList.MasterScanOrderCount - 1)
                ReDim Preserve scanList.MasterScanNumList(scanList.MasterScanOrderCount - 1)
                ReDim Preserve scanList.MasterScanTimeList(scanList.MasterScanOrderCount - 1)

                If scanList.MasterScanOrderCount <= 0 Then
                    ' No scans found
                    If scansOutOfRange > 0 Then
                        ReportWarning("None of the spectra in the input file was within the specified scan number and/or scan time range: " & filePath)
                        SetLocalErrorCode(eMasicErrorCodes.NoParentIonsFoundInInputFile)
                    Else
                        ReportError("No scans found in the input file: " & filePath)
                        SetLocalErrorCode(eMasicErrorCodes.InputFileAccessError)
                    End If

                    Return False
                End If

                success = True

                Console.WriteLine()

            Catch ex As Exception
                ReportError("Error in ExtractScanInfoFromMSXMLDataFile", ex, eMasicErrorCodes.InputFileDataReadError)
            End Try

            ' Record the current memory usage (before we close the .mzXML file)
            OnUpdateMemoryUsage()

            ' Close the handle to the data file
            If Not objXMLReader Is Nothing Then
                Try
                    objXMLReader.CloseFile()
                Catch ex As Exception
                    ' Ignore errors here
                End Try
            End If

            Return success

        End Function

        Private Sub ExtractScanInfoWork(
          scanList As clsScanList,
          objSpectraCache As clsSpectraCache,
          dataOutputHandler As clsDataOutput,
          sicOptions As clsSICOptions,
          objMSSpectrum As clsMSSpectrum,
          objSpectrumInfo As clsSpectrumInfo,
          ByRef lastSurveyScanIndex As Integer,
          ByRef lastSurveyScanIndexInMasterSeqOrder As Integer,
          ByRef lastNonZoomSurveyScanIndex As Integer,
          ByRef warnCount As Integer,
          keepRawSpectra As Boolean,
          keepMSMSSpectra As Boolean)

            Dim isMzXML As Boolean
            Dim eMRMScanType As MRMScanTypeConstants

            ' ReSharper disable once NotAccessedVariable
            Dim msDataResolution As Double

            Dim objMZXmlSpectrumInfo As clsSpectrumInfoMzXML = Nothing

            If TypeOf (objSpectrumInfo) Is clsSpectrumInfoMzXML Then
                objMZXmlSpectrumInfo = CType(objSpectrumInfo, clsSpectrumInfoMzXML)
                isMzXML = True
            Else
                isMzXML = False
            End If

            ' Determine if this was an MS/MS scan
            ' If yes, determine the scan number of the survey scan
            If objSpectrumInfo.MSLevel <= 1 Then
                ' Survey Scan

                Dim newSurveyScan = New clsScanInfo()
                With newSurveyScan
                    .ScanNumber = objSpectrumInfo.ScanNumber
                    .ScanTime = objSpectrumInfo.RetentionTimeMin

                    ' If this is a mzXML file that was processed with ReadW, then .ScanHeaderText and .ScanTypeName will get updated by UpdateMSXMLScanType
                    .ScanHeaderText = String.Empty

                    ' This may get updated via the call to UpdateMSXmlScanType()
                    .ScanTypeName = "MS"

                    .BasePeakIonMZ = objSpectrumInfo.BasePeakMZ
                    .BasePeakIonIntensity = objSpectrumInfo.BasePeakIntensity

                    ' Survey scans typically lead to multiple parent ions; we do not record them here
                    .FragScanInfo.ParentIonInfoIndex = -1
                    .TotalIonIntensity = CSng(Math.Min(objSpectrumInfo.TotalIonCurrent, Single.MaxValue))

                    ' Determine the minimum positive intensity in this scan
                    .MinimumPositiveIntensity = mPeakFinder.FindMinimumPositiveValue(objMSSpectrum.IonCount, objMSSpectrum.IonsIntensity, 0)

                    ' If this is a mzXML file that was processed with ReadW, then these values will get updated by UpdateMSXMLScanType
                    .ZoomScan = False
                    .SIMScan = False
                    .MRMScanType = MRMScanTypeConstants.NotMRM

                    .LowMass = objSpectrumInfo.mzRangeStart
                    .HighMass = objSpectrumInfo.mzRangeEnd
                    .IsFTMS = False

                End With

                scanList.SurveyScans.Add(newSurveyScan)

                UpdateMSXmlScanType(newSurveyScan, objSpectrumInfo.MSLevel, "MS", isMzXML, objMZXmlSpectrumInfo)

                lastSurveyScanIndex = scanList.SurveyScans.Count - 1

                scanList.AddMasterScanEntry(clsScanList.eScanTypeConstants.SurveyScan, lastSurveyScanIndex)
                lastSurveyScanIndexInMasterSeqOrder = scanList.MasterScanOrderCount - 1

                If mOptions.SICOptions.SICToleranceIsPPM Then
                    ' Define MSDataResolution based on the tolerance value that will be used at the lowest m/z in this spectrum, divided by sicOptions.CompressToleranceDivisorForPPM
                    ' However, if the lowest m/z value is < 100, then use 100 m/z
                    If objSpectrumInfo.mzRangeStart < 100 Then
                        msDataResolution = clsParentIonProcessing.GetParentIonToleranceDa(sicOptions, 100) / sicOptions.CompressToleranceDivisorForPPM
                    Else
                        msDataResolution = clsParentIonProcessing.GetParentIonToleranceDa(sicOptions, objSpectrumInfo.mzRangeStart) / sicOptions.CompressToleranceDivisorForPPM
                    End If
                Else
                    msDataResolution = sicOptions.SICTolerance / sicOptions.CompressToleranceDivisorForDa
                End If


                ' Note: Even if keepRawSpectra = False, we still need to load the raw data so that we can compute the noise level for the spectrum
                StoreMzXmlSpectrum(
                    objMSSpectrum,
                    newSurveyScan,
                    objSpectraCache,
                    sicOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions,
                    DISCARD_LOW_INTENSITY_MS_DATA_ON_LOAD,
                    sicOptions.CompressMSSpectraData,
                    sicOptions.SimilarIonMZToleranceHalfWidth / sicOptions.CompressToleranceDivisorForDa,
                    keepRawSpectra)

                SaveScanStatEntry(dataOutputHandler.OutputFileHandles.ScanStats,
                                  clsScanList.eScanTypeConstants.SurveyScan, newSurveyScan, sicOptions.DatasetNumber)

            Else
                ' Fragmentation Scan

                Dim newFragScan = New clsScanInfo()
                With newFragScan
                    .ScanNumber = objSpectrumInfo.ScanNumber
                    .ScanTime = objSpectrumInfo.RetentionTimeMin

                    ' If this is a mzXML file that was processed with ReadW, then .ScanHeaderText and .ScanTypeName will get updated by UpdateMSXMLScanType
                    .ScanHeaderText = String.Empty

                    ' This may get updated via the call to UpdateMSXmlScanType()
                    .ScanTypeName = "MSn"

                    .BasePeakIonMZ = objSpectrumInfo.BasePeakMZ
                    .BasePeakIonIntensity = objSpectrumInfo.BasePeakIntensity

                    ' 1 for the first MS/MS scan after the survey scan, 2 for the second one, etc.
                    .FragScanInfo.FragScanNumber = (scanList.MasterScanOrderCount - 1) - lastSurveyScanIndexInMasterSeqOrder
                    .FragScanInfo.MSLevel = objSpectrumInfo.MSLevel

                    .TotalIonIntensity = CSng(Math.Min(objSpectrumInfo.TotalIonCurrent, Single.MaxValue))

                    ' Determine the minimum positive intensity in this scan
                    .MinimumPositiveIntensity = mPeakFinder.FindMinimumPositiveValue(objMSSpectrum.IonCount, objMSSpectrum.IonsIntensity, 0)

                    ' If this is a mzXML file that was processed with ReadW, then these values will get updated by UpdateMSXMLScanType
                    .ZoomScan = False
                    .SIMScan = False
                    .MRMScanType = MRMScanTypeConstants.NotMRM

                    .MRMScanInfo.MRMMassCount = 0

                End With

                UpdateMSXmlScanType(newFragScan, objSpectrumInfo.MSLevel, "MSn", isMzXML, objMZXmlSpectrumInfo)

                eMRMScanType = newFragScan.MRMScanType
                If Not eMRMScanType = MRMScanTypeConstants.NotMRM Then
                    ' This is an MRM scan
                    scanList.MRMDataPresent = True

                    Dim scanInfo = New ThermoRawFileReader.clsScanInfo(objSpectrumInfo.SpectrumID)

                    With scanInfo
                        .FilterText = newFragScan.ScanHeaderText
                        .MRMScanType = eMRMScanType
                        .MRMInfo = New MRMInfo()

                        If Not String.IsNullOrEmpty(.FilterText) Then
                            ' Parse out the MRM_QMS or SRM information for this scan
                            XRawFileIO.ExtractMRMMasses(.FilterText, .MRMScanType, .MRMInfo)
                        Else
                            ' .MZRangeStart and .MZRangeEnd should be equivalent, and they should define the m/z of the MRM transition

                            If objSpectrumInfo.mzRangeEnd - objSpectrumInfo.mzRangeStart >= 0.5 Then
                                ' The data is likely MRM and not SRM
                                ' We cannot currently handle data like this
                                ' (would need to examine the mass values  and find the clumps of data to infer the transitions present)
                                warnCount += 1
                                If warnCount <= 5 Then
                                    ReportError("Warning: m/z range for SRM scan " & objSpectrumInfo.ScanNumber & " is " &
                                                (objSpectrumInfo.mzRangeEnd - objSpectrumInfo.mzRangeStart).ToString("0.0") &
                                                " m/z; this is likely a MRM scan, but MASIC doesn't support inferring the " &
                                                "MRM transition masses from the observed m/z values.  Results will likely not be meaningful")
                                    If warnCount = 5 Then
                                        ReportMessage("Additional m/z range warnings will not be shown")
                                    End If
                                End If
                            End If

                            Dim mRMMassRange As udtMRMMassRangeType
                            mRMMassRange = New udtMRMMassRangeType()
                            With mRMMassRange
                                .StartMass = objSpectrumInfo.mzRangeStart
                                .EndMass = objSpectrumInfo.mzRangeEnd
                                .CentralMass = Math.Round(.StartMass + (.EndMass - .StartMass) / 2, 6)
                            End With
                            .MRMInfo.MRMMassList.Add(mRMMassRange)

                        End If
                    End With

                    newFragScan.MRMScanInfo = clsMRMProcessing.DuplicateMRMInfo(scanInfo.MRMInfo, objSpectrumInfo.ParentIonMZ)

                    If scanList.SurveyScans.Count = 0 Then
                        ' Need to add a "fake" survey scan that we can map this parent ion to
                        lastNonZoomSurveyScanIndex = scanList.AddFakeSurveyScan()
                    End If
                Else
                    newFragScan.MRMScanInfo.MRMMassCount = 0
                End If

                With newFragScan
                    .LowMass = objSpectrumInfo.mzRangeStart
                    .HighMass = objSpectrumInfo.mzRangeEnd
                    .IsFTMS = False
                End With

                scanList.FragScans.Add(newFragScan)

                scanList.AddMasterScanEntry(clsScanList.eScanTypeConstants.FragScan, scanList.FragScans.Count - 1)

                ' Note: Even if keepRawSpectra = False, we still need to load the raw data so that we can compute the noise level for the spectrum
                StoreMzXmlSpectrum(
                    objMSSpectrum,
                    newFragScan,
                    objSpectraCache,
                    sicOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions,
                    DISCARD_LOW_INTENSITY_MSMS_DATA_ON_LOAD,
                    sicOptions.CompressMSMSSpectraData,
                    mOptions.BinningOptions.BinSize / sicOptions.CompressToleranceDivisorForDa,
                    keepRawSpectra AndAlso keepMSMSSpectra)

                SaveScanStatEntry(dataOutputHandler.OutputFileHandles.ScanStats,
                                  clsScanList.eScanTypeConstants.FragScan, newFragScan, sicOptions.DatasetNumber)

                If eMRMScanType = MRMScanTypeConstants.NotMRM Then
                    ' This is not an MRM scan
                    mParentIonProcessor.AddUpdateParentIons(scanList, lastSurveyScanIndex, objSpectrumInfo.ParentIonMZ,
                                                            scanList.FragScans.Count - 1, objSpectraCache, sicOptions)
                Else
                    ' This is an MRM scan
                    mParentIonProcessor.AddUpdateParentIons(scanList, lastNonZoomSurveyScanIndex, objSpectrumInfo.ParentIonMZ,
                                                            newFragScan.MRMScanInfo, objSpectraCache, sicOptions)
                End If

            End If
        End Sub

        Private Sub StoreMzXmlSpectrum(
          objMSSpectrum As clsMSSpectrum,
          scanInfo As clsScanInfo,
          objSpectraCache As clsSpectraCache,
          noiseThresholdOptions As MASICPeakFinder.clsBaselineNoiseOptions,
          discardLowIntensityData As Boolean,
          compressSpectraData As Boolean,
          msDataResolution As Double,
          keepRawSpectrum As Boolean)

            Try

                If objMSSpectrum.IonsMZ Is Nothing OrElse objMSSpectrum.IonsIntensity Is Nothing Then
                    scanInfo.IonCount = 0
                    scanInfo.IonCountRaw = 0
                Else
                    objMSSpectrum.IonCount = objMSSpectrum.IonsMZ.Length

                    scanInfo.IonCount = objMSSpectrum.IonCount
                    scanInfo.IonCountRaw = scanInfo.IonCount
                End If

                objMSSpectrum.ScanNumber = scanInfo.ScanNumber

                If scanInfo.IonCount > 0 Then
                    With scanInfo
                        ' Confirm the total scan intensity stored in the mzXML file
                        Dim totalIonIntensity As Single = 0
                        For ionIndex = 0 To objMSSpectrum.IonCount - 1
                            totalIonIntensity += objMSSpectrum.IonsIntensity(ionIndex)
                        Next

                        If .TotalIonIntensity < Single.Epsilon Then
                            .TotalIonIntensity = totalIonIntensity
                        End If

                    End With

                    mScanTracking.ProcessAndStoreSpectrum(
                        scanInfo, Me,
                        objSpectraCache, objMSSpectrum,
                        noiseThresholdOptions,
                        discardLowIntensityData,
                        compressSpectraData,
                        msDataResolution,
                        keepRawSpectrum)
                Else
                    scanInfo.TotalIonIntensity = 0
                End If

            Catch ex As Exception
                ReportError("Error in clsMasic->StoreMzXMLSpectrum ", ex)
            End Try

        End Sub

        Private Sub UpdateMSXmlScanType(
          scanInfo As clsScanInfo,
          msLevel As Integer,
          defaultScanType As String,
          isMzXML As Boolean,
          ByRef objMZXmlSpectrumInfo As clsSpectrumInfoMzXML)

            If Not isMzXML Then
                ' Not a .mzXML file
                ' Use the defaults
                scanInfo.ScanHeaderText = String.Empty
                scanInfo.ScanTypeName = defaultScanType
                Return

            End If

            ' Store the filter line text in .ScanHeaderText
            ' Only Thermo files processed with ReadW will have a FilterLine
            scanInfo.ScanHeaderText = objMZXmlSpectrumInfo.FilterLine

            If Not String.IsNullOrEmpty(scanInfo.ScanHeaderText) Then
                ' This is a Thermo file; auto define .ScanTypeName using the FilterLine text
                scanInfo.ScanTypeName = XRawFileIO.GetScanTypeNameFromFinniganScanFilterText(scanInfo.ScanHeaderText)

                With scanInfo
                    ' Now populate .SIMScan, .MRMScanType and .ZoomScan
                    Dim msLevelFromFilter As Integer
                    XRawFileIO.ValidateMSScan(.ScanHeaderText, msLevelFromFilter, .SIMScan, .MRMScanType, .ZoomScan)
                End With
                Return

            End If

            scanInfo.ScanHeaderText = String.Empty
            scanInfo.ScanTypeName = objMZXmlSpectrumInfo.ScanType

            If String.IsNullOrEmpty(scanInfo.ScanTypeName) Then
                scanInfo.ScanTypeName = defaultScanType
            Else
                ' Possibly update .ScanTypeName to match the values returned by XRawFileIO.GetScanTypeNameFromFinniganScanFilterText()
                Select Case scanInfo.ScanTypeName.ToLower()
                    Case clsSpectrumInfoMzXML.ScanTypeNames.Full.ToLower()
                        If msLevel <= 1 Then
                            scanInfo.ScanTypeName = "MS"
                        Else
                            scanInfo.ScanTypeName = "MSn"
                        End If

                    Case clsSpectrumInfoMzXML.ScanTypeNames.zoom.ToLower()
                        scanInfo.ScanTypeName = "Zoom-MS"

                    Case clsSpectrumInfoMzXML.ScanTypeNames.MRM.ToLower()
                        scanInfo.ScanTypeName = "MRM"
                        scanInfo.MRMScanType = MRMScanTypeConstants.SRM

                    Case clsSpectrumInfoMzXML.ScanTypeNames.SRM.ToLower()
                        scanInfo.ScanTypeName = "CID-SRM"
                        scanInfo.MRMScanType = MRMScanTypeConstants.SRM
                    Case Else
                        ' Leave .ScanTypeName unchanged
                End Select
            End If

            If Not String.IsNullOrWhiteSpace(objMZXmlSpectrumInfo.ActivationMethod) Then
                ' Update ScanTypeName to include the activation method,
                ' For example, to be CID-MSn instead of simply MSn
                scanInfo.ScanTypeName = objMZXmlSpectrumInfo.ActivationMethod & "-" & scanInfo.ScanTypeName

                If scanInfo.ScanTypeName = "HCD-MSn" Then
                    ' HCD spectra are always high res; auto-update things
                    scanInfo.ScanTypeName = "HCD-HMSn"
                End If

            End If


        End Sub

    End Class

End Namespace