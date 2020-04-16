﻿Option Strict On

' This class will read an LC-MS/MS data file and create selected ion chromatograms
'   for each of the parent ion masses chosen for fragmentation
' It will create several output files, including a BPI for the survey scan,
'   a BPI for the fragmentation scans, an XML file containing the SIC data
'   for each parent ion, and a "flat file" ready for import into the database
'   containing summaries of the SIC data statistics
' Supported file types are Thermo .Raw files (LCQ, LTQ, LTQ-FT),
'   Agilent Ion Trap (.MGF and .CDF files), and mzXML files

' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Program started October 11, 2003
' Copyright 2005, Battelle Memorial Institute.  All Rights Reserved.

' E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
' Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
' -------------------------------------------------------------------------------
'
' Licensed under the 2-Clause BSD License; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at
' https://opensource.org/licenses/BSD-2-Clause

Imports System.Runtime.InteropServices
Imports MASIC.DataOutput
Imports MASIC.DatasetStats
Imports PRISM
Imports PRISM.Logging

Public Class clsMASIC
    Inherits FileProcessor.ProcessFilesBase

    ''' <summary>
    ''' Constructor
    ''' </summary>
    Public Sub New()
        MyBase.mFileDate = "March 27, 2020"

        mLocalErrorCode = eMasicErrorCodes.NoError
        mStatusMessage = String.Empty

        mProcessingStats = New clsProcessingStats()
        InitializeMemoryManagementOptions(mProcessingStats)

        mMASICPeakFinder = New MASICPeakFinder.clsMASICPeakFinder()
        RegisterEvents(mMASICPeakFinder)

        Options = New clsMASICOptions(Me.FileVersion(), mMASICPeakFinder.ProgramVersion)
        Options.InitializeVariables()
        RegisterEvents(Options)

    End Sub

#Region "Constants and Enums"

    ' Enabling this will result in SICs with less noise, which will hurt noise determination after finding the SICs
    Public Const DISCARD_LOW_INTENSITY_MS_DATA_ON_LOAD As Boolean = False

    ' Disabling this will slow down the correlation process (slightly)
    Public Const DISCARD_LOW_INTENSITY_MSMS_DATA_ON_LOAD As Boolean = True

    Private Const MINIMUM_STATUS_FILE_UPDATE_INTERVAL_SECONDS As Integer = 3

    Public Enum eProcessingStepConstants
        NewTask = 0
        ReadDataFile = 1
        SaveBPI = 2
        CreateSICsAndFindPeaks = 3
        FindSimilarParentIons = 4
        SaveExtendedScanStatsFiles = 5
        SaveSICStatsFlatFile = 6
        CloseOpenFileHandles = 7
        UpdateXMLFileWithNewOptimalPeakApexValues = 8
        Cancelled = 99
        Complete = 100
    End Enum

    Public Enum eMasicErrorCodes
        NoError = 0
        InvalidDatasetLookupFilePath = 1
        UnknownFileExtension = 2            ' This error code matches the identical code in clsFilterMsMsSpectra
        InputFileAccessError = 4            ' This error code matches the identical code in clsFilterMsMsSpectra
        InvalidDatasetID = 8
        CreateSICsError = 16
        FindSICPeaksError = 32
        InvalidCustomSICValues = 64
        NoParentIonsFoundInInputFile = 128
        NoSurveyScansFoundInInputFile = 256
        FindSimilarParentIonsError = 512
        InputFileDataReadError = 1024
        OutputFileWriteError = 2048
        FileIOPermissionsError = 4096
        ErrorCreatingSpectrumCacheDirectory = 8192
        ErrorCachingSpectrum = 16384
        ErrorUncachingSpectrum = 32768
        ErrorDeletingCachedSpectrumFiles = 65536
        UnspecifiedError = -1
    End Enum

#End Region

#Region "Classwide Variables"

    Private mLoggedMASICVersion As Boolean = False

    Private ReadOnly mMASICPeakFinder As MASICPeakFinder.clsMASICPeakFinder

    Private ReadOnly mProcessingStats As clsProcessingStats

    ''' <summary>
    ''' Current processing step
    ''' </summary>
    Private mProcessingStep As eProcessingStepConstants

    ''' <summary>
    ''' Percent completion for the current sub task
    ''' </summary>
    ''' <remarks>Value between 0 and 100</remarks>
    Private mSubtaskProcessingStepPct As Single

    Private mSubtaskDescription As String = String.Empty

    Private mLocalErrorCode As eMasicErrorCodes
    Private mStatusMessage As String

#End Region

#Region "Events"
    ''' <summary>
    ''' Use RaiseEvent MyBase.ProgressChanged when updating the overall progress
    ''' Use ProgressSubtaskChanged when updating the sub task progress
    ''' </summary>
    Public Event ProgressSubtaskChanged()

    Public Event ProgressResetKeypressAbort()

#End Region

    ' ReSharper disable UnusedMember.Global

#Region "Processing Options and File Path Interface Functions"

    <Obsolete("Use Property Options")>
    Public Property DatabaseConnectionString As String
        Get
            Return Options.DatabaseConnectionString
        End Get
        Set
            Options.DatabaseConnectionString = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property DatasetInfoQuerySql As String
        Get
            Return Options.DatasetInfoQuerySql
        End Get
        Set
            Options.DatasetInfoQuerySql = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property DatasetLookupFilePath As String
        Get
            Return Options.DatasetLookupFilePath
        End Get
        Set
            Options.DatasetLookupFilePath = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property DatasetNumber As Integer
        Get
            Return Options.SICOptions.DatasetID
        End Get
        Set
            Options.SICOptions.DatasetID = Value
        End Set
    End Property

    Public ReadOnly Property LocalErrorCode As eMasicErrorCodes
        Get
            Return mLocalErrorCode
        End Get
    End Property

    Public ReadOnly Property MASICPeakFinderDllVersion As String
        Get
            If Not mMASICPeakFinder Is Nothing Then
                Return mMASICPeakFinder.ProgramVersion
            Else
                Return String.Empty
            End If
        End Get
    End Property

    <Obsolete("Use Property Options")>
    Public Property MASICStatusFilename As String
        Get
            Return Options.MASICStatusFilename
        End Get
        Set
            If Value Is Nothing OrElse Value.Trim.Length = 0 Then
                Options.MASICStatusFilename = clsMASICOptions.DEFAULT_MASIC_STATUS_FILE_NAME
            Else
                Options.MASICStatusFilename = Value
            End If
        End Set
    End Property

    Public ReadOnly Property Options As clsMASICOptions

    Public ReadOnly Property ProcessStep As eProcessingStepConstants
        Get
            Return mProcessingStep
        End Get
    End Property

    ''' <summary>
    ''' Subtask progress percent complete
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks>Value between 0 and 100</remarks>
    Public ReadOnly Property SubtaskProgressPercentComplete As Single
        Get
            Return mSubtaskProcessingStepPct
        End Get
    End Property

    Public ReadOnly Property SubtaskDescription As String
        Get
            Return mSubtaskDescription
        End Get
    End Property

    Public ReadOnly Property StatusMessage As String
        Get
            Return mStatusMessage
        End Get
    End Property
#End Region

#Region "SIC Options Interface Functions"
    <Obsolete("Use Property Options")>
    Public Property CDFTimeInSeconds As Boolean
        Get
            Return Options.CDFTimeInSeconds
        End Get
        Set
            Options.CDFTimeInSeconds = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property CompressMSSpectraData As Boolean
        Get
            Return Options.SICOptions.CompressMSSpectraData
        End Get
        Set
            Options.SICOptions.CompressMSSpectraData = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property CompressMSMSSpectraData As Boolean
        Get
            Return Options.SICOptions.CompressMSMSSpectraData
        End Get
        Set
            Options.SICOptions.CompressMSMSSpectraData = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property CompressToleranceDivisorForDa As Double
        Get
            Return Options.SICOptions.CompressToleranceDivisorForDa
        End Get
        Set
            Options.SICOptions.CompressToleranceDivisorForDa = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property CompressToleranceDivisorForPPM As Double
        Get
            Return Options.SICOptions.CompressToleranceDivisorForPPM
        End Get
        Set
            Options.SICOptions.CompressToleranceDivisorForPPM = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ConsolidateConstantExtendedHeaderValues As Boolean
        Get
            Return Options.ConsolidateConstantExtendedHeaderValues
        End Get
        Set
            Options.ConsolidateConstantExtendedHeaderValues = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public ReadOnly Property CustomSICListScanType As clsCustomSICList.eCustomSICScanTypeConstants
        Get
            Return Options.CustomSICList.ScanToleranceType
        End Get
    End Property

    <Obsolete("Use Property Options")>
    Public ReadOnly Property CustomSICListScanTolerance As Single
        Get
            Return Options.CustomSICList.ScanOrAcqTimeTolerance
        End Get
    End Property

    <Obsolete("Use Property Options")>
    Public ReadOnly Property CustomSICListSearchValues As List(Of clsCustomMZSearchSpec)
        Get
            Return Options.CustomSICList.CustomMZSearchValues
        End Get
    End Property

    <Obsolete("Use Property Options")>
    Public Property CustomSICListFileName As String
        Get
            Return Options.CustomSICList.CustomSICListFileName
        End Get
        Set
            Options.CustomSICList.CustomSICListFileName = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataOnly As Boolean
        Get
            Return Options.ExportRawDataOnly
        End Get
        Set
            Options.ExportRawDataOnly = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property FastExistingXMLFileTest As Boolean
        Get
            Return Options.FastExistingXMLFileTest
        End Get
        Set
            Options.FastExistingXMLFileTest = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property IncludeHeadersInExportFile As Boolean
        Get
            Return Options.IncludeHeadersInExportFile
        End Get
        Set
            Options.IncludeHeadersInExportFile = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property IncludeScanTimesInSICStatsFile As Boolean
        Get
            Return Options.IncludeScanTimesInSICStatsFile
        End Get
        Set
            Options.IncludeScanTimesInSICStatsFile = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property LimitSearchToCustomMZList As Boolean
        Get
            Return Options.CustomSICList.LimitSearchToCustomMZList
        End Get
        Set
            Options.CustomSICList.LimitSearchToCustomMZList = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ParentIonDecoyMassDa As Double
        Get
            Return Options.ParentIonDecoyMassDa
        End Get
        Set
            Options.ParentIonDecoyMassDa = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SkipMSMSProcessing As Boolean
        Get
            Return Options.SkipMSMSProcessing
        End Get
        Set
            Options.SkipMSMSProcessing = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SkipSICAndRawDataProcessing As Boolean
        Get
            Return Options.SkipSICAndRawDataProcessing
        End Get
        Set
            Options.SkipSICAndRawDataProcessing = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SuppressNoParentIonsError As Boolean
        Get
            Return Options.SuppressNoParentIonsError
        End Get
        Set
            Options.SuppressNoParentIonsError = Value
        End Set
    End Property

    <Obsolete("No longer supported")>
    Public Property UseFinniganXRawAccessorFunctions As Boolean
        Get
            Return True
        End Get
        Set

        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteDetailedSICDataFile As Boolean
        Get
            Return Options.WriteDetailedSICDataFile
        End Get
        Set
            Options.WriteDetailedSICDataFile = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteExtendedStats As Boolean
        Get
            Return Options.WriteExtendedStats
        End Get
        Set
            Options.WriteExtendedStats = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteExtendedStatsIncludeScanFilterText As Boolean
        Get
            Return Options.WriteExtendedStatsIncludeScanFilterText
        End Get
        Set
            Options.WriteExtendedStatsIncludeScanFilterText = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteExtendedStatsStatusLog As Boolean
        Get
            Return Options.WriteExtendedStatsStatusLog
        End Get
        Set
            Options.WriteExtendedStatsStatusLog = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteMSMethodFile As Boolean
        Get
            Return Options.WriteMSMethodFile
        End Get
        Set
            Options.WriteMSMethodFile = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property WriteMSTuneFile As Boolean
        Get
            Return Options.WriteMSTuneFile
        End Get
        Set
            Options.WriteMSTuneFile = Value
        End Set
    End Property

    ''' <summary>
    ''' This property is included for historical reasons since SIC tolerance can now be Da or PPM
    ''' </summary>
    ''' <returns></returns>
    <Obsolete("Use Property Options.  Also, the SICToleranceDa setting should not be used; use SetSICTolerance and GetSICTolerance instead")>
    Public Property SICToleranceDa As Double
        Get
            Return Options.SICOptions.SICToleranceDa
        End Get
        Set
            Options.SICOptions.SICToleranceDa = Value
        End Set
    End Property

    <Obsolete("Use Property Options.SICOptions.GetSICTolerance")>
    Public Function GetSICTolerance() As Double
        Dim toleranceIsPPM As Boolean
        Return Options.SICOptions.GetSICTolerance(toleranceIsPPM)
    End Function

    <Obsolete("Use Property Options.SICOptions.GetSICTolerance")>
    Public Function GetSICTolerance(<Out> ByRef toleranceIsPPM As Boolean) As Double
        Return Options.SICOptions.GetSICTolerance(toleranceIsPPM)
    End Function

    <Obsolete("Use Property Options.SICOptions.SetSICTolerance")>
    Public Sub SetSICTolerance(sicTolerance As Double, toleranceIsPPM As Boolean)
        Options.SICOptions.SetSICTolerance(sicTolerance, toleranceIsPPM)
    End Sub

    <Obsolete("Use Property Options")>
    Public Property SICToleranceIsPPM As Boolean
        Get
            Return Options.SICOptions.SICToleranceIsPPM
        End Get
        Set
            Options.SICOptions.SICToleranceIsPPM = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property RefineReportedParentIonMZ As Boolean
        Get
            Return Options.SICOptions.RefineReportedParentIonMZ
        End Get
        Set
            Options.SICOptions.RefineReportedParentIonMZ = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property RTRangeEnd As Single
        Get
            Return Options.SICOptions.RTRangeEnd
        End Get
        Set
            Options.SICOptions.RTRangeEnd = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property RTRangeStart As Single
        Get
            Return Options.SICOptions.RTRangeStart
        End Get
        Set
            Options.SICOptions.RTRangeStart = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ScanRangeEnd As Integer
        Get
            Return Options.SICOptions.ScanRangeEnd
        End Get
        Set
            Options.SICOptions.ScanRangeEnd = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ScanRangeStart As Integer
        Get
            Return Options.SICOptions.ScanRangeStart
        End Get
        Set
            Options.SICOptions.ScanRangeStart = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MaxSICPeakWidthMinutesBackward As Single
        Get
            Return Options.SICOptions.MaxSICPeakWidthMinutesBackward
        End Get
        Set
            Options.SICOptions.MaxSICPeakWidthMinutesBackward = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MaxSICPeakWidthMinutesForward As Single
        Get
            Return Options.SICOptions.MaxSICPeakWidthMinutesForward
        End Get
        Set
            Options.SICOptions.MaxSICPeakWidthMinutesForward = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SICNoiseFractionLowIntensityDataToAverage As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SICNoiseMinimumSignalToNoiseRatio As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.MinimumSignalToNoiseRatio
        End Get
        Set
            ' This value isn't utilized by MASIC for SICs so we'll force it to always be zero
            Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.MinimumSignalToNoiseRatio = 0
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SICNoiseThresholdIntensity As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.BaselineNoiseLevelAbsolute
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.BaselineNoiseLevelAbsolute = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SICNoiseThresholdMode As MASICPeakFinder.clsMASICPeakFinder.eNoiseThresholdModes
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.BaselineNoiseMode
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.SICBaselineNoiseOptions.BaselineNoiseMode = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MassSpectraNoiseFractionLowIntensityDataToAverage As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.TrimmedMeanFractionLowIntensityDataToAverage
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.TrimmedMeanFractionLowIntensityDataToAverage = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MassSpectraNoiseMinimumSignalToNoiseRatio As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.MinimumSignalToNoiseRatio
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.MinimumSignalToNoiseRatio = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MassSpectraNoiseThresholdIntensity As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.BaselineNoiseLevelAbsolute
        End Get
        Set
            If Value < 0 Or Value > Double.MaxValue Then Value = 0
            Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.BaselineNoiseLevelAbsolute = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MassSpectraNoiseThresholdMode As MASICPeakFinder.clsMASICPeakFinder.eNoiseThresholdModes
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.BaselineNoiseMode
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.MassSpectraNoiseThresholdOptions.BaselineNoiseMode = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ReplaceSICZeroesWithMinimumPositiveValueFromMSData As Boolean
        Get
            Return Options.SICOptions.ReplaceSICZeroesWithMinimumPositiveValueFromMSData
        End Get
        Set
            Options.SICOptions.ReplaceSICZeroesWithMinimumPositiveValueFromMSData = Value
        End Set
    End Property
#End Region

#Region "Raw Data Export Options"

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataIncludeMSMS As Boolean
        Get
            Return Options.RawDataExportOptions.IncludeMSMS
        End Get
        Set
            Options.RawDataExportOptions.IncludeMSMS = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataRenumberScans As Boolean
        Get
            Return Options.RawDataExportOptions.RenumberScans
        End Get
        Set
            Options.RawDataExportOptions.RenumberScans = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataIntensityMinimum As Single
        Get
            Return Options.RawDataExportOptions.IntensityMinimum
        End Get
        Set
            Options.RawDataExportOptions.IntensityMinimum = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataMaxIonCountPerScan As Integer
        Get
            Return Options.RawDataExportOptions.MaxIonCountPerScan
        End Get
        Set
            Options.RawDataExportOptions.MaxIonCountPerScan = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataFileFormat As clsRawDataExportOptions.eExportRawDataFileFormatConstants
        Get
            Return Options.RawDataExportOptions.FileFormat
        End Get
        Set
            Options.RawDataExportOptions.FileFormat = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawDataMinimumSignalToNoiseRatio As Single
        Get
            Return Options.RawDataExportOptions.MinimumSignalToNoiseRatio
        End Get
        Set
            Options.RawDataExportOptions.MinimumSignalToNoiseRatio = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ExportRawSpectraData As Boolean
        Get
            Return Options.RawDataExportOptions.ExportEnabled
        End Get
        Set
            Options.RawDataExportOptions.ExportEnabled = Value
        End Set
    End Property

#End Region

#Region "Peak Finding Options"
    <Obsolete("Use Property Options")>
    Public Property IntensityThresholdAbsoluteMinimum As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.IntensityThresholdAbsoluteMinimum
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.IntensityThresholdAbsoluteMinimum = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property IntensityThresholdFractionMax As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.IntensityThresholdFractionMax
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.IntensityThresholdFractionMax = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MaxDistanceScansNoOverlap As Integer
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MaxDistanceScansNoOverlap
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.MaxDistanceScansNoOverlap = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property FindPeaksOnSmoothedData As Boolean
        Get
            Return Options.SICOptions.SICPeakFinderOptions.FindPeaksOnSmoothedData
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.FindPeaksOnSmoothedData = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SmoothDataRegardlessOfMinimumPeakWidth As Boolean
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SmoothDataRegardlessOfMinimumPeakWidth
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.SmoothDataRegardlessOfMinimumPeakWidth = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property UseButterworthSmooth As Boolean
        Get
            Return Options.SICOptions.SICPeakFinderOptions.UseButterworthSmooth
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.UseButterworthSmooth = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ButterworthSamplingFrequency As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.ButterworthSamplingFrequency
        End Get
        Set
            ' Value should be between 0.01 and 0.99; this is checked for in the filter, so we don't need to check here
            Options.SICOptions.SICPeakFinderOptions.ButterworthSamplingFrequency = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property ButterworthSamplingFrequencyDoubledForSIMData As Boolean
        Get
            Return Options.SICOptions.SICPeakFinderOptions.ButterworthSamplingFrequencyDoubledForSIMData
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.ButterworthSamplingFrequencyDoubledForSIMData = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property UseSavitzkyGolaySmooth As Boolean
        Get
            Return Options.SICOptions.SICPeakFinderOptions.UseSavitzkyGolaySmooth
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.UseSavitzkyGolaySmooth = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SavitzkyGolayFilterOrder As Short
        Get
            Return Options.SICOptions.SICPeakFinderOptions.SavitzkyGolayFilterOrder
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.SavitzkyGolayFilterOrder = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SaveSmoothedData As Boolean
        Get
            Return Options.SICOptions.SaveSmoothedData
        End Get
        Set
            Options.SICOptions.SaveSmoothedData = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MaxAllowedUpwardSpikeFractionMax As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.MaxAllowedUpwardSpikeFractionMax
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.MaxAllowedUpwardSpikeFractionMax = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property InitialPeakWidthScansScaler As Double
        Get
            Return Options.SICOptions.SICPeakFinderOptions.InitialPeakWidthScansScaler
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.InitialPeakWidthScansScaler = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property InitialPeakWidthScansMaximum As Integer
        Get
            Return Options.SICOptions.SICPeakFinderOptions.InitialPeakWidthScansMaximum
        End Get
        Set
            Options.SICOptions.SICPeakFinderOptions.InitialPeakWidthScansMaximum = Value
        End Set
    End Property
#End Region

#Region "Spectrum Similarity Options"
    <Obsolete("Use Property Options")>
    Public Property SimilarIonMZToleranceHalfWidth As Single
        Get
            Return Options.SICOptions.SimilarIonMZToleranceHalfWidth
        End Get
        Set
            Options.SICOptions.SimilarIonMZToleranceHalfWidth = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SimilarIonToleranceHalfWidthMinutes As Single
        Get
            Return Options.SICOptions.SimilarIonToleranceHalfWidthMinutes
        End Get
        Set
            Options.SICOptions.SimilarIonToleranceHalfWidthMinutes = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SpectrumSimilarityMinimum As Single
        Get
            Return Options.SICOptions.SpectrumSimilarityMinimum
        End Get
        Set
            Options.SICOptions.SpectrumSimilarityMinimum = Value
        End Set
    End Property
#End Region

#Region "Binning Options Interface Functions"

    <Obsolete("Use Property Options")>
    Public Property BinStartX As Single
        Get
            Return Options.BinningOptions.StartX
        End Get
        Set
            Options.BinningOptions.StartX = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property BinEndX As Single
        Get
            Return Options.BinningOptions.EndX
        End Get
        Set
            Options.BinningOptions.EndX = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property BinSize As Single
        Get
            Return Options.BinningOptions.BinSize
        End Get
        Set
            Options.BinningOptions.BinSize = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property BinnedDataIntensityPrecisionPercent As Single
        Get
            Return Options.BinningOptions.IntensityPrecisionPercent
        End Get
        Set
            Options.BinningOptions.IntensityPrecisionPercent = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property NormalizeBinnedData As Boolean
        Get
            Return Options.BinningOptions.Normalize
        End Get
        Set
            Options.BinningOptions.Normalize = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property SumAllIntensitiesForBin As Boolean
        Get
            Return Options.BinningOptions.SumAllIntensitiesForBin
        End Get
        Set
            Options.BinningOptions.SumAllIntensitiesForBin = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property MaximumBinCount As Integer
        Get
            Return Options.BinningOptions.MaximumBinCount
        End Get
        Set
            Options.BinningOptions.MaximumBinCount = Value
        End Set
    End Property
#End Region

#Region "Memory Options Interface Functions"

    <Obsolete("Use Property Options")>
    Public Property DiskCachingAlwaysDisabled As Boolean
        Get
            Return Options.CacheOptions.DiskCachingAlwaysDisabled
        End Get
        Set
            Options.CacheOptions.DiskCachingAlwaysDisabled = Value
        End Set
    End Property

    <Obsolete("Use Property Options")>
    Public Property CacheDirectoryPath As String
        Get
            Return Options.CacheOptions.DirectoryPath
        End Get
        Set
            Options.CacheOptions.DirectoryPath = Value
        End Set
    End Property

    <Obsolete("Legacy parameter; no longer used")>
    Public Property CacheMaximumMemoryUsageMB As Single
        Get
            Return Options.CacheOptions.MaximumMemoryUsageMB
        End Get
        Set
            Options.CacheOptions.MaximumMemoryUsageMB = Value
        End Set
    End Property

    <Obsolete("Legacy parameter; no longer used")>
    Public Property CacheMinimumFreeMemoryMB As Single
        Get
            Return Options.CacheOptions.MinimumFreeMemoryMB
        End Get
        Set
            If Options.CacheOptions.MinimumFreeMemoryMB < 10 Then
                Options.CacheOptions.MinimumFreeMemoryMB = 10
            End If
            Options.CacheOptions.MinimumFreeMemoryMB = Value
        End Set
    End Property

    <Obsolete("Legacy parameter; no longer used")>
    Public Property CacheSpectraToRetainInMemory As Integer
        Get
            Return Options.CacheOptions.SpectraToRetainInMemory
        End Get
        Set
            Options.CacheOptions.SpectraToRetainInMemory = Value
        End Set
    End Property

#End Region

    ' ReSharper restore UnusedMember.Global

    Public Overrides Sub AbortProcessingNow()
        AbortProcessing = True
        Options.AbortProcessing = True
    End Sub

    Private Function FindSICsAndWriteOutput(
      inputFilePathFull As String,
      outputDirectoryPath As String,
      scanList As clsScanList,
      spectraCache As clsSpectraCache,
      dataOutputHandler As clsDataOutput,
      scanTracking As clsScanTracking,
      datasetFileInfo As DatasetFileInfo,
      parentIonProcessor As clsParentIonProcessing,
      dataImporterBase As DataInput.clsDataImport
      ) As Boolean

        Dim success = True
        Dim inputFileName = Path.GetFileName(inputFilePathFull)
        Dim similarParentIonUpdateCount As Integer

        Try
            Dim bpiWriter = New clsBPIWriter()
            RegisterEvents(bpiWriter)

            Dim xmlResultsWriter = New clsXMLResultsWriter(Options)
            RegisterEvents(xmlResultsWriter)

            '---------------------------------------------------------
            ' Save the BPIs and TICs
            '---------------------------------------------------------

            UpdateProcessingStep(eProcessingStepConstants.SaveBPI)
            UpdateOverallProgress("Processing Data for " & inputFileName)
            SetSubtaskProcessingStepPct(0, "Saving chromatograms to disk")
            UpdatePeakMemoryUsage()

            If Options.SkipSICAndRawDataProcessing OrElse Not Options.ExportRawDataOnly Then
                LogMessage("ProcessFile: Call SaveBPIs")
                bpiWriter.SaveBPIs(scanList, spectraCache, inputFilePathFull, outputDirectoryPath)
            End If

            '---------------------------------------------------------
            ' Close the ScanStats file handle
            '---------------------------------------------------------
            Try
                LogMessage("ProcessFile: Close outputFileHandles.ScanStats")

                dataOutputHandler.OutputFileHandles.CloseScanStats()

            Catch ex As Exception
                ' Ignore errors here
            End Try

            '---------------------------------------------------------
            ' Create the DatasetInfo XML file
            '---------------------------------------------------------

            LogMessage("ProcessFile: Create DatasetInfo File")
            dataOutputHandler.CreateDatasetInfoFile(inputFileName, outputDirectoryPath, scanTracking, datasetFileInfo)

            If Options.SkipSICAndRawDataProcessing Then
                LogMessage("ProcessFile: Skipping SIC Processing")

                SetDefaultPeakLocValues(scanList)
            Else

                '---------------------------------------------------------
                ' Optionally, export the raw mass spectra data
                '---------------------------------------------------------

                If Options.RawDataExportOptions.ExportEnabled Then
                    Dim rawDataExporter = New clsSpectrumDataWriter(bpiWriter, Options)
                    RegisterEvents(rawDataExporter)

                    rawDataExporter.ExportRawDataToDisk(scanList, spectraCache, inputFileName, outputDirectoryPath)
                End If

                If Options.ReporterIons.ReporterIonStatsEnabled Then
                    ' Look for Reporter Ions in the Fragmentation spectra

                    Dim reporterIonProcessor = New clsReporterIonProcessor(Options)
                    RegisterEvents(reporterIonProcessor)
                    reporterIonProcessor.FindReporterIons(scanList, spectraCache, inputFilePathFull, outputDirectoryPath)
                End If

                Dim mrmProcessor = New clsMRMProcessing(Options, dataOutputHandler)
                RegisterEvents(mrmProcessor)

                '---------------------------------------------------------
                ' If MRM data is present, save the MRM values to disk
                '---------------------------------------------------------
                If scanList.MRMDataPresent Then
                    mrmProcessor.ExportMRMDataToDisk(scanList, spectraCache, inputFileName, outputDirectoryPath)
                End If

                If Not Options.ExportRawDataOnly Then

                    '---------------------------------------------------------
                    ' Add the custom SIC values to scanList
                    '---------------------------------------------------------
                    Options.CustomSICList.AddCustomSICValues(scanList, Options.SICOptions.SICTolerance,
                                                             Options.SICOptions.SICToleranceIsPPM, Options.CustomSICList.ScanOrAcqTimeTolerance)


                    '---------------------------------------------------------
                    ' Possibly create the Tab-separated values SIC details output file
                    '---------------------------------------------------------
                    If Options.WriteDetailedSICDataFile Then
                        success = dataOutputHandler.InitializeSICDetailsTextFile(inputFilePathFull, outputDirectoryPath)
                        If Not success Then
                            SetLocalErrorCode(eMasicErrorCodes.OutputFileWriteError)
                            Exit Try
                        End If
                    End If

                    '---------------------------------------------------------
                    ' Create the XML output file
                    '---------------------------------------------------------
                    success = xmlResultsWriter.XMLOutputFileInitialize(inputFilePathFull, outputDirectoryPath, dataOutputHandler, scanList, spectraCache, Options.SICOptions, Options.BinningOptions)
                    If Not success Then
                        SetLocalErrorCode(eMasicErrorCodes.OutputFileWriteError)
                        Exit Try
                    End If

                    '---------------------------------------------------------
                    ' Create the selected ion chromatograms (SICs)
                    ' For each one, find the peaks and make an entry to the XML output file
                    '---------------------------------------------------------

                    UpdateProcessingStep(eProcessingStepConstants.CreateSICsAndFindPeaks)
                    SetSubtaskProcessingStepPct(0)
                    UpdatePeakMemoryUsage()

                    LogMessage("ProcessFile: Call CreateParentIonSICs")
                    Dim sicProcessor = New clsSICProcessing(mMASICPeakFinder, mrmProcessor)
                    RegisterEvents(sicProcessor)

                    success = sicProcessor.CreateParentIonSICs(scanList, spectraCache, Options, dataOutputHandler, sicProcessor, xmlResultsWriter)

                    If Not success Then
                        SetLocalErrorCode(eMasicErrorCodes.CreateSICsError, True)
                        Exit Try
                    End If

                End If

                If Not (Options.SkipMSMSProcessing OrElse Options.ExportRawDataOnly) Then

                    '---------------------------------------------------------
                    ' Find Similar Parent Ions
                    '---------------------------------------------------------

                    UpdateProcessingStep(eProcessingStepConstants.FindSimilarParentIons)
                    SetSubtaskProcessingStepPct(0)
                    UpdatePeakMemoryUsage()

                    LogMessage("ProcessFile: Call FindSimilarParentIons")
                    success = parentIonProcessor.FindSimilarParentIons(scanList, spectraCache, Options, dataImporterBase, similarParentIonUpdateCount)

                    If Not success Then
                        SetLocalErrorCode(eMasicErrorCodes.FindSimilarParentIonsError, True)
                        Exit Try
                    End If
                End If

            End If

            If Options.WriteExtendedStats AndAlso Not Options.ExportRawDataOnly Then
                '---------------------------------------------------------
                ' Save Extended Scan Stats Files
                '---------------------------------------------------------

                UpdateProcessingStep(eProcessingStepConstants.SaveExtendedScanStatsFiles)
                SetSubtaskProcessingStepPct(0)
                UpdatePeakMemoryUsage()

                LogMessage("ProcessFile: Call SaveExtendedScanStatsFiles")
                success = dataOutputHandler.ExtendedStatsWriter.SaveExtendedScanStatsFiles(
                    scanList, inputFileName, outputDirectoryPath, Options.IncludeHeadersInExportFile)

                If Not success Then
                    SetLocalErrorCode(eMasicErrorCodes.OutputFileWriteError, True)
                    Exit Try
                End If
            End If


            '---------------------------------------------------------
            ' Save SIC Stats Flat File
            '---------------------------------------------------------

            UpdateProcessingStep(eProcessingStepConstants.SaveSICStatsFlatFile)
            SetSubtaskProcessingStepPct(0)
            UpdatePeakMemoryUsage()

            If Not Options.ExportRawDataOnly Then

                Dim sicStatsWriter = New clsSICStatsWriter()
                RegisterEvents(sicStatsWriter)

                LogMessage("ProcessFile: Call SaveSICStatsFlatFile")
                success = sicStatsWriter.SaveSICStatsFlatFile(scanList, inputFileName, outputDirectoryPath, Options, dataOutputHandler)

                If Not success Then
                    SetLocalErrorCode(eMasicErrorCodes.OutputFileWriteError, True)
                    Exit Try
                End If
            End If


            UpdateProcessingStep(eProcessingStepConstants.CloseOpenFileHandles)
            SetSubtaskProcessingStepPct(0)
            UpdatePeakMemoryUsage()

            If Not (Options.SkipSICAndRawDataProcessing OrElse Options.ExportRawDataOnly) Then

                '---------------------------------------------------------
                ' Write processing stats to the XML output file
                '---------------------------------------------------------

                LogMessage("ProcessFile: Call FinalizeXMLFile")
                Dim processingTimeSec = GetTotalProcessingTimeSec()
                success = xmlResultsWriter.XMLOutputFileFinalize(dataOutputHandler, scanList, spectraCache,
                                                                 mProcessingStats, processingTimeSec)

            End If

            '---------------------------------------------------------
            ' Close any open output files
            '---------------------------------------------------------
            dataOutputHandler.OutputFileHandles.CloseAll()

            '---------------------------------------------------------
            ' Save a text file containing the headers used in the text files
            '---------------------------------------------------------
            If Not Options.IncludeHeadersInExportFile Then
                LogMessage("ProcessFile: Call SaveHeaderGlossary")
                dataOutputHandler.SaveHeaderGlossary(scanList, inputFileName, outputDirectoryPath)
            End If

            If Not (Options.SkipSICAndRawDataProcessing OrElse Options.ExportRawDataOnly) AndAlso similarParentIonUpdateCount > 0 Then
                '---------------------------------------------------------
                ' Reopen the XML file and update the entries for those ions in scanList that had their
                ' Optimal peak apex scan numbers updated
                '---------------------------------------------------------

                UpdateProcessingStep(eProcessingStepConstants.UpdateXMLFileWithNewOptimalPeakApexValues)
                SetSubtaskProcessingStepPct(0)
                UpdatePeakMemoryUsage()

                LogMessage("ProcessFile: Call XmlOutputFileUpdateEntries")
                xmlResultsWriter.XmlOutputFileUpdateEntries(scanList, inputFileName, outputDirectoryPath)
            End If

        Catch ex As Exception
            success = False
            LogErrors("FindSICsAndWriteOutput", "Error saving results to: " & outputDirectoryPath, ex, eMasicErrorCodes.OutputFileWriteError)
        End Try

        Return success

    End Function

    Public Overrides Function GetDefaultExtensionsToParse() As IList(Of String)
        Return DataInput.clsDataImport.GetDefaultExtensionsToParse()
    End Function

    Public Overrides Function GetErrorMessage() As String
        ' Returns String.Empty if no error

        Dim errorMessage As String

        If MyBase.ErrorCode = ProcessFilesErrorCodes.LocalizedError OrElse
           MyBase.ErrorCode = ProcessFilesErrorCodes.NoError Then
            Select Case mLocalErrorCode
                Case eMasicErrorCodes.NoError
                    errorMessage = String.Empty
                Case eMasicErrorCodes.InvalidDatasetLookupFilePath
                    errorMessage = "Invalid dataset lookup file path"
                Case eMasicErrorCodes.UnknownFileExtension
                    errorMessage = "Unknown file extension"
                Case eMasicErrorCodes.InputFileAccessError
                    errorMessage = "Input file access error"
                Case eMasicErrorCodes.InvalidDatasetID
                    errorMessage = "Invalid dataset number"
                Case eMasicErrorCodes.CreateSICsError
                    errorMessage = "Create SIC's error"
                Case eMasicErrorCodes.FindSICPeaksError
                    errorMessage = "Error finding SIC peaks"
                Case eMasicErrorCodes.InvalidCustomSICValues
                    errorMessage = "Invalid custom SIC values"
                Case eMasicErrorCodes.NoParentIonsFoundInInputFile
                    errorMessage = "No parent ions were found in the input file (additionally, no custom SIC values were defined)"
                Case eMasicErrorCodes.NoSurveyScansFoundInInputFile
                    errorMessage = "No survey scans were found in the input file (do you have a Scan Range filter defined?)"
                Case eMasicErrorCodes.FindSimilarParentIonsError
                    errorMessage = "Find similar parent ions error"
                Case eMasicErrorCodes.FindSimilarParentIonsError
                    errorMessage = "Find similar parent ions error"
                Case eMasicErrorCodes.InputFileDataReadError
                    errorMessage = "Error reading data from input file"
                Case eMasicErrorCodes.OutputFileWriteError
                    errorMessage = "Error writing data to output file"
                Case eMasicErrorCodes.FileIOPermissionsError
                    errorMessage = "File IO Permissions Error"
                Case eMasicErrorCodes.ErrorCreatingSpectrumCacheDirectory
                    errorMessage = "Error creating spectrum cache directory"
                Case eMasicErrorCodes.ErrorCachingSpectrum
                    errorMessage = "Error caching spectrum"
                Case eMasicErrorCodes.ErrorUncachingSpectrum
                    errorMessage = "Error uncaching spectrum"
                Case eMasicErrorCodes.ErrorDeletingCachedSpectrumFiles
                    errorMessage = "Error deleting cached spectrum files"
                Case eMasicErrorCodes.UnspecifiedError
                    errorMessage = "Unspecified localized error"
                Case Else
                    ' This shouldn't happen
                    errorMessage = "Unknown error state"
            End Select
        Else
            errorMessage = MyBase.GetBaseClassErrorMessage()
        End If

        Return errorMessage

    End Function

    Private Function GetFreeMemoryMB() As Single
        ' Returns the amount of free memory, in MB

        Dim freeMemoryMB = SystemInfo.GetFreeMemoryMB()

        Return freeMemoryMB

    End Function

    Private Function GetProcessMemoryUsageMB() As Single

        ' Obtain a handle to the current process
        Dim objProcess = Process.GetCurrentProcess()

        ' The WorkingSet is the total physical memory usage
        Return CSng(objProcess.WorkingSet64 / 1024 / 1024)

    End Function

    Private Function GetTotalProcessingTimeSec() As Single

        Dim objProcess = Process.GetCurrentProcess()

        Return CSng(objProcess.TotalProcessorTime().TotalSeconds)

    End Function

    Private Sub InitializeMemoryManagementOptions(processingStats As clsProcessingStats)

        With processingStats
            .PeakMemoryUsageMB = GetProcessMemoryUsageMB()
            .TotalProcessingTimeAtStart = GetTotalProcessingTimeSec()
            .CacheEventCount = 0
            .UnCacheEventCount = 0

            .FileLoadStartTime = DateTime.UtcNow
            .FileLoadEndTime = .FileLoadStartTime

            .ProcessingStartTime = .FileLoadStartTime
            .ProcessingEndTime = .FileLoadStartTime

            .MemoryUsageMBAtStart = .PeakMemoryUsageMB
            .MemoryUsageMBDuringLoad = .PeakMemoryUsageMB
            .MemoryUsageMBAtEnd = .PeakMemoryUsageMB
        End With

    End Sub

    Private Function LoadData(
      inputFilePathFull As String,
      outputDirectoryPath As String,
      dataOutputHandler As clsDataOutput,
      parentIonProcessor As clsParentIonProcessing,
      scanTracking As clsScanTracking,
      scanList As clsScanList,
      spectraCache As clsSpectraCache,
      <Out> ByRef dataImporterBase As DataInput.clsDataImport,
      <Out> ByRef datasetFileInfo As DatasetFileInfo
      ) As Boolean

        Dim success As Boolean
        datasetFileInfo = New DatasetFileInfo()

        Try

            '---------------------------------------------------------
            ' Define inputFileName (which is referenced several times below)
            '---------------------------------------------------------
            Dim inputFileName = Path.GetFileName(inputFilePathFull)

            '---------------------------------------------------------
            ' Create the _ScanStats.txt file
            '---------------------------------------------------------
            dataOutputHandler.OpenOutputFileHandles(inputFileName, outputDirectoryPath, Options.IncludeHeadersInExportFile)

            '---------------------------------------------------------
            ' Read the mass spectra from the input data file
            '---------------------------------------------------------

            UpdateProcessingStep(eProcessingStepConstants.ReadDataFile)
            SetSubtaskProcessingStepPct(0)
            UpdatePeakMemoryUsage()
            mStatusMessage = String.Empty

            If Options.SkipSICAndRawDataProcessing Then
                Options.ExportRawDataOnly = False
            End If

            Dim keepRawMSSpectra = Not Options.SkipSICAndRawDataProcessing OrElse Options.ExportRawDataOnly

            Options.SICOptions.ValidateSICOptions()

            Select Case Path.GetExtension(inputFileName).ToUpper()
                Case DataInput.clsDataImport.THERMO_RAW_FILE_EXTENSION.ToUpper()

                    ' Open the .Raw file and obtain the scan information

                    Dim dataImporter = New DataInput.clsDataImportThermoRaw(Options, mMASICPeakFinder, parentIonProcessor, scanTracking)
                    RegisterDataImportEvents(dataImporter)
                    dataImporterBase = dataImporter

                    success = dataImporter.ExtractScanInfoFromXcaliburDataFile(
                      inputFilePathFull,
                      scanList, spectraCache, dataOutputHandler,
                      keepRawMSSpectra,
                      Not Options.SkipMSMSProcessing)

                    datasetFileInfo = dataImporter.DatasetFileInfo

                Case DataInput.clsDataImport.MZ_ML_FILE_EXTENSION.ToUpper()

                    ' Open the .mzML file and obtain the scan information

                    Dim dataImporter = New DataInput.clsDataImportMSXml(Options, mMASICPeakFinder, parentIonProcessor, scanTracking)
                    RegisterDataImportEvents(dataImporter)
                    dataImporterBase = dataImporter

                    success = dataImporter.ExtractScanInfoFromMzMLDataFile(
                        inputFilePathFull,
                        scanList, spectraCache, dataOutputHandler,
                        keepRawMSSpectra,
                        Not Options.SkipMSMSProcessing)

                    datasetFileInfo = dataImporter.DatasetFileInfo

                Case DataInput.clsDataImport.MZ_XML_FILE_EXTENSION1.ToUpper(),
                     DataInput.clsDataImport.MZ_XML_FILE_EXTENSION2.ToUpper()

                    ' Open the .mzXML file and obtain the scan information

                    Dim dataImporter = New DataInput.clsDataImportMSXml(Options, mMASICPeakFinder, parentIonProcessor, scanTracking)
                    RegisterDataImportEvents(dataImporter)
                    dataImporterBase = dataImporter

                    success = dataImporter.ExtractScanInfoFromMzXMLDataFile(
                      inputFilePathFull,
                      scanList, spectraCache, dataOutputHandler,
                      keepRawMSSpectra,
                      Not Options.SkipMSMSProcessing)

                    datasetFileInfo = dataImporter.DatasetFileInfo

                Case DataInput.clsDataImport.MZ_DATA_FILE_EXTENSION1.ToUpper(),
                     DataInput.clsDataImport.MZ_DATA_FILE_EXTENSION2.ToUpper()

                    ' Open the .mzData file and obtain the scan information

                    Dim dataImporter = New DataInput.clsDataImportMSXml(Options, mMASICPeakFinder, parentIonProcessor, scanTracking)
                    RegisterDataImportEvents(dataImporter)
                    dataImporterBase = dataImporter

                    success = dataImporter.ExtractScanInfoFromMzDataFile(
                      inputFilePathFull,
                      scanList, spectraCache, dataOutputHandler,
                      keepRawMSSpectra, Not Options.SkipMSMSProcessing)

                    datasetFileInfo = dataImporter.DatasetFileInfo

                Case DataInput.clsDataImport.AGILENT_MSMS_FILE_EXTENSION.ToUpper(),
                     DataInput.clsDataImport.AGILENT_MS_FILE_EXTENSION.ToUpper()

                    ' Open the .MGF and .CDF files to obtain the scan information

                    Dim dataImporter = New DataInput.clsDataImportMGFandCDF(Options, mMASICPeakFinder, parentIonProcessor, scanTracking)
                    RegisterDataImportEvents(dataImporter)
                    dataImporterBase = dataImporter

                    success = dataImporter.ExtractScanInfoFromMGFandCDF(
                      inputFilePathFull,
                      scanList, spectraCache, dataOutputHandler,
                      keepRawMSSpectra, Not Options.SkipMSMSProcessing)

                    datasetFileInfo = dataImporter.DatasetFileInfo

                Case Else
                    mStatusMessage = "Unknown file extension: " & Path.GetExtension(inputFilePathFull)
                    SetLocalErrorCode(eMasicErrorCodes.UnknownFileExtension)
                    success = False

                    ' Instantiate this object to avoid a warning below about the object potentially not being initialized
                    ' In reality, an Exit Try statement will be reached and the potentially problematic use will therefore not get encountered
                    datasetFileInfo = New DatasetFileInfo()
                    dataImporterBase = Nothing
            End Select

            If Not success Then
                If mLocalErrorCode = eMasicErrorCodes.NoParentIonsFoundInInputFile AndAlso String.IsNullOrWhiteSpace(mStatusMessage) Then
                    mStatusMessage = "None of the spectra in the input file was within the specified scan number and/or scan time range"
                End If
                SetLocalErrorCode(eMasicErrorCodes.InputFileAccessError, True)
            End If

        Catch ex As Exception
            success = False
            LogErrors("ProcessFile", "Error accessing input data file: " & inputFilePathFull, ex, eMasicErrorCodes.InputFileDataReadError)
            dataImporterBase = Nothing
        End Try

        Return success

    End Function

    Public Function LoadParameterFileSettings(parameterFilePath As String) As Boolean
        Dim success = Options.LoadParameterFileSettings(parameterFilePath)
        Return success
    End Function

    Private Sub LogErrors(
      source As String,
      message As String,
      ex As Exception,
      Optional eNewErrorCode As eMasicErrorCodes = eMasicErrorCodes.NoError)

        Dim messageWithoutCRLF As String

        Options.StatusMessage = message

        messageWithoutCRLF = Options.StatusMessage.Replace(ControlChars.NewLine, "; ")

        If ex Is Nothing Then
            ex = New Exception("Error")
        Else
            If Not ex.Message Is Nothing AndAlso ex.Message.Length > 0 AndAlso Not message.Contains(ex.Message) Then
                messageWithoutCRLF &= "; " & ex.Message
            End If
        End If

        ' Show the message and log to the clsProcessFilesBaseClass logger
        If String.IsNullOrEmpty(source) Then
            ShowErrorMessage(messageWithoutCRLF, True)
        Else
            ShowErrorMessage(source & ": " & messageWithoutCRLF, True)
        End If

        If Not ex Is Nothing Then
            Console.WriteLine(StackTraceFormatter.GetExceptionStackTraceMultiLine(ex))
        End If

        If Not eNewErrorCode = eMasicErrorCodes.NoError Then
            SetLocalErrorCode(eNewErrorCode, True)
        End If

    End Sub

    ''' <summary>
    ''' Main processing function
    ''' </summary>
    ''' <param name="inputFilePath"></param>
    ''' <param name="outputDirectoryPath"></param>
    ''' <param name="parameterFilePath"></param>
    ''' <param name="resetErrorCode"></param>
    ''' <returns></returns>
    Public Overloads Overrides Function ProcessFile(
      inputFilePath As String,
      outputDirectoryPath As String,
      parameterFilePath As String,
      resetErrorCode As Boolean) As Boolean

        Dim inputFileInfo As FileInfo

        Dim success, doNotProcess As Boolean

        Dim inputFilePathFull As String = String.Empty

        If Not mLoggedMASICVersion Then
            LogMessage("Starting MASIC v" & GetAppVersion(mFileDate))
            Console.WriteLine()
            mLoggedMASICVersion = True
        End If

        If resetErrorCode Then
            SetLocalErrorCode(eMasicErrorCodes.NoError)
        End If

        Options.OutputDirectoryPath = outputDirectoryPath

        mSubtaskProcessingStepPct = 0
        UpdateProcessingStep(eProcessingStepConstants.NewTask, True)
        MyBase.ResetProgress("Starting calculations")

        mStatusMessage = String.Empty

        UpdateStatusFile(True)

        If Not Options.LoadParameterFileSettings(parameterFilePath, inputFilePath) Then
            MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.InvalidParameterFile)
            mStatusMessage = "Parameter file load error: " & parameterFilePath

            MyBase.ShowErrorMessage(mStatusMessage)

            If MyBase.ErrorCode = ProcessFilesErrorCodes.NoError Then
                MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.InvalidParameterFile)
            End If
            UpdateProcessingStep(eProcessingStepConstants.Cancelled, True)

            LogMessage("Processing ended in error")
            Return False
        End If

        Dim dataOutputHandler = New clsDataOutput(Options)
        RegisterEvents(dataOutputHandler)

        Try
            ' If a Custom SICList file is defined, then load the custom SIC values now
            If Options.CustomSICList.CustomSICListFileName.Length > 0 Then
                Dim sicListReader = New DataInput.clsCustomSICListReader(Options.CustomSICList)
                RegisterEvents(sicListReader)

                LogMessage("ProcessFile: Reading custom SIC values file: " & Options.CustomSICList.CustomSICListFileName)
                success = sicListReader.LoadCustomSICListFromFile(Options.CustomSICList.CustomSICListFileName)
                If Not success Then
                    SetLocalErrorCode(eMasicErrorCodes.InvalidCustomSICValues)
                    Exit Try
                End If
            End If

            Options.ReporterIons.UpdateMZIntensityFilterIgnoreRange()

            LogMessage("Source data file: " & inputFilePath)

            If inputFilePath Is Nothing OrElse inputFilePath.Length = 0 Then
                ShowErrorMessage("Input file name is empty")
                MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.InvalidInputFilePath)
                Exit Try
            End If

            mStatusMessage = "Parsing " & Path.GetFileName(inputFilePath)

            success = CleanupFilePaths(inputFilePath, outputDirectoryPath)
            Options.OutputDirectoryPath = outputDirectoryPath

            If success Then
                Dim dbAccessor = New clsDatabaseAccess(Options)
                RegisterEvents(dbAccessor)

                Options.SICOptions.DatasetID = dbAccessor.LookupDatasetID(inputFilePath, Options.DatasetLookupFilePath, Options.SICOptions.DatasetID)

                If Me.LocalErrorCode <> eMasicErrorCodes.NoError Then
                    If Me.LocalErrorCode = eMasicErrorCodes.InvalidDatasetID OrElse Me.LocalErrorCode = eMasicErrorCodes.InvalidDatasetLookupFilePath Then
                        ' Ignore this error
                        Me.SetLocalErrorCode(eMasicErrorCodes.NoError)
                        success = True
                    Else
                        success = False
                    End If
                End If
            End If

            If Not success Then
                If mLocalErrorCode = eMasicErrorCodes.NoError Then MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.FilePathError)
                Exit Try
            End If

            Try
                '---------------------------------------------------------
                ' See if an output XML file already exists
                ' If it does, open it and read the parameters used
                ' If they match the current analysis parameters, and if the input file specs match the input file, then
                '  do not reprocess
                '---------------------------------------------------------

                ' Obtain the full path to the input file
                inputFileInfo = New FileInfo(inputFilePath)
                inputFilePathFull = inputFileInfo.FullName

                LogMessage("Checking for existing results in the output path: " & outputDirectoryPath)

                doNotProcess = dataOutputHandler.CheckForExistingResults(inputFilePathFull, outputDirectoryPath, Options)

                If doNotProcess Then
                    LogMessage("Existing results found; data will not be reprocessed")
                End If

            Catch ex As Exception
                success = False
                LogErrors("ProcessFile", "Error checking for existing results file", ex, eMasicErrorCodes.InputFileDataReadError)
            End Try

            If doNotProcess Then
                success = True
                Exit Try
            End If

            Try
                '---------------------------------------------------------
                ' Verify that we have write access to the output directory
                '---------------------------------------------------------

                ' The following should work for testing access permissions, but it doesn't
                'Dim objFilePermissionTest As New Security.Permissions.FileIOPermission(Security.Permissions.FileIOPermissionAccess.AllAccess, outputDirectoryPath)
                '' The following should throw an exception if the current user doesn't have read/write access; however, no exception is thrown for me
                'objFilePermissionTest.Demand()
                'objFilePermissionTest.Assert()

                LogMessage("Checking for write permission in the output path: " & outputDirectoryPath)

                Dim outputFileTestPath As String = Path.Combine(outputDirectoryPath, "TestOutputFile" & DateTime.UtcNow.Ticks & ".tmp")

                Using fsOutFileTest As New StreamWriter(outputFileTestPath, False)
                    fsOutFileTest.WriteLine("Test")
                End Using

                ' Wait 250 msec, then delete the file
                Threading.Thread.Sleep(250)
                File.Delete(outputFileTestPath)

            Catch ex As Exception
                success = False
                LogErrors("ProcessFile", "The current user does not have write permission for the output directory: " & outputDirectoryPath, ex, eMasicErrorCodes.FileIOPermissionsError)
            End Try

            If Not success Then
                SetLocalErrorCode(eMasicErrorCodes.FileIOPermissionsError)
                Exit Try
            End If

            '---------------------------------------------------------
            ' Reset the processing stats
            '---------------------------------------------------------

            InitializeMemoryManagementOptions(mProcessingStats)

            '---------------------------------------------------------
            ' Instantiate the SpectraCache
            '---------------------------------------------------------

            Using spectraCache = New clsSpectraCache(Options.CacheOptions) With {
                .DiskCachingAlwaysDisabled = Options.CacheOptions.DiskCachingAlwaysDisabled,
                .CacheDirectoryPath = Options.CacheOptions.DirectoryPath,
                .CacheSpectraToRetainInMemory = Options.CacheOptions.SpectraToRetainInMemory
            }
                RegisterEvents(spectraCache)

                spectraCache.InitializeSpectraPool()

                Dim datasetFileInfo = New DatasetFileInfo()

                Dim scanList = New clsScanList()
                RegisterEvents(scanList)

                Dim parentIonProcessor = New clsParentIonProcessing(Options.ReporterIons)
                RegisterEvents(parentIonProcessor)

                Dim scanTracking = New clsScanTracking(Options.ReporterIons, mMASICPeakFinder)
                RegisterEvents(scanTracking)

                Dim dataImporterBase As DataInput.clsDataImport = Nothing

                '---------------------------------------------------------
                ' Load the mass spectral data
                '---------------------------------------------------------

                success = LoadData(inputFilePathFull,
                                   outputDirectoryPath,
                                   dataOutputHandler,
                                   parentIonProcessor,
                                   scanTracking,
                                   scanList,
                                   spectraCache,
                                   dataImporterBase,
                                   datasetFileInfo)

                ' Record that the file is finished loading
                mProcessingStats.FileLoadEndTime = DateTime.UtcNow

                If Not success Then
                    If mStatusMessage Is Nothing OrElse mStatusMessage.Length = 0 Then
                        mStatusMessage = "Unable to parse file; unknown error"
                    Else
                        mStatusMessage = "Unable to parse file: " & mStatusMessage
                    End If

                    ShowErrorMessage(mStatusMessage)
                    Exit Try
                End If

                If success Then
                    '---------------------------------------------------------
                    ' Find the Selected Ion Chromatograms, reporter ions, etc. and write the results to disk
                    '---------------------------------------------------------

                    success = FindSICsAndWriteOutput(
                        inputFilePathFull, outputDirectoryPath,
                        scanList, spectraCache, dataOutputHandler, scanTracking,
                        datasetFileInfo, parentIonProcessor, dataImporterBase)
                End If

            End Using

        Catch ex As Exception
            success = False
            LogErrors("ProcessFile", "Error in ProcessFile", ex, eMasicErrorCodes.UnspecifiedError)
        Finally

            ' Record the final processing stats (before the output file handles are closed)
            With mProcessingStats
                .ProcessingEndTime = DateTime.UtcNow
                .MemoryUsageMBAtEnd = GetProcessMemoryUsageMB()
            End With

            '---------------------------------------------------------
            ' Make sure the output file handles are closed
            '---------------------------------------------------------

            dataOutputHandler.OutputFileHandles.CloseAll()
        End Try

        Try
            '---------------------------------------------------------
            ' Cleanup after processing or error
            '---------------------------------------------------------

            LogMessage("ProcessFile: Processing nearly complete")

            Console.WriteLine()
            If doNotProcess Then
                mStatusMessage = "Existing valid results were found; processing was not repeated."
                ShowMessage(mStatusMessage)
            ElseIf success Then
                mStatusMessage = "Processing complete.  Results can be found in directory: " & outputDirectoryPath
                ShowMessage(mStatusMessage)
            Else
                If Me.LocalErrorCode = eMasicErrorCodes.NoError Then
                    mStatusMessage = "Error Code " & MyBase.ErrorCode & ": " & Me.GetErrorMessage()
                    ShowErrorMessage(mStatusMessage)
                Else
                    mStatusMessage = "Error Code " & Me.LocalErrorCode & ": " & Me.GetErrorMessage()
                    ShowErrorMessage(mStatusMessage)
                End If
            End If

            With mProcessingStats
                LogMessage("ProcessingStats: Memory Usage At Start (MB) = " & .MemoryUsageMBAtStart.ToString())
                LogMessage("ProcessingStats: Peak memory usage (MB) = " & .PeakMemoryUsageMB.ToString())

                LogMessage("ProcessingStats: File Load Time (seconds) = " & .FileLoadEndTime.Subtract(.FileLoadStartTime).TotalSeconds.ToString())
                LogMessage("ProcessingStats: Memory Usage During Load (MB) = " & .MemoryUsageMBDuringLoad.ToString())

                LogMessage("ProcessingStats: Processing Time (seconds) = " & .ProcessingEndTime.Subtract(.ProcessingStartTime).TotalSeconds.ToString())
                LogMessage("ProcessingStats: Memory Usage At End (MB) = " & .MemoryUsageMBAtEnd.ToString())

                LogMessage("ProcessingStats: Cache Event Count = " & .CacheEventCount.ToString())
                LogMessage("ProcessingStats: UncCache Event Count = " & .UnCacheEventCount.ToString())
            End With

            If success Then
                LogMessage("Processing complete")
            Else
                LogMessage("Processing ended in error")
            End If

        Catch ex As Exception
            success = False
            LogErrors("ProcessFile", "Error in ProcessFile (Cleanup)", ex, eMasicErrorCodes.UnspecifiedError)
        End Try

        If success Then
            Options.SICOptions.DatasetID += 1
        End If

        If success Then
            UpdateProcessingStep(eProcessingStepConstants.Complete, True)
        Else
            UpdateProcessingStep(eProcessingStepConstants.Cancelled, True)
        End If

        Return success

    End Function

    Private Sub RegisterDataImportEvents(dataImporter As DataInput.clsDataImport)
        RegisterEvents(dataImporter)
        AddHandler dataImporter.UpdateMemoryUsageEvent, AddressOf UpdateMemoryUsageEventHandler
    End Sub

    Private Sub RegisterEventsBase(oClass As IEventNotifier)
        AddHandler oClass.StatusEvent, AddressOf MessageEventHandler
        AddHandler oClass.ErrorEvent, AddressOf ErrorEventHandler
        AddHandler oClass.WarningEvent, AddressOf WarningEventHandler
        AddHandler oClass.ProgressUpdate, AddressOf ProgressUpdateHandler
    End Sub

    Private Overloads Sub RegisterEvents(oClass As clsMasicEventNotifier)
        RegisterEventsBase(oClass)

        AddHandler oClass.UpdateCacheStatsEvent, AddressOf UpdatedCacheStatsEventHandler
        AddHandler oClass.UpdateBaseClassErrorCodeEvent, AddressOf UpdateBaseClassErrorCodeEventHandler
        AddHandler oClass.UpdateErrorCodeEvent, AddressOf UpdateErrorCodeEventHandler
    End Sub

    ' ReSharper disable UnusedMember.Global

    <Obsolete("Use Options.SaveParameterFileSettings")>
    Public Function SaveParameterFileSettings(parameterFilePath As String) As Boolean
        Dim success = Options.SaveParameterFileSettings(parameterFilePath)
        Return success
    End Function

    <Obsolete("Use Options.ReporterIons.SetReporterIons")>
    Public Sub SetReporterIons(reporterIonMZList() As Double)
        Options.ReporterIons.SetReporterIons(reporterIonMZList)
    End Sub

    <Obsolete("Use Options.ReporterIons.SetReporterIons")>
    Public Sub SetReporterIons(reporterIonMZList() As Double, mzToleranceDa As Double)
        Options.ReporterIons.SetReporterIons(reporterIonMZList, mzToleranceDa)
    End Sub

    <Obsolete("Use Options.ReporterIons.SetReporterIons")>
    Public Sub SetReporterIons(reporterIons As List(Of clsReporterIonInfo))
        Options.ReporterIons.SetReporterIons(reporterIons, True)
    End Sub

    <Obsolete("Use Options.ReporterIons.SetReporterIonMassMode")>
    Public Sub SetReporterIonMassMode(eReporterIonMassMode As clsReporterIons.eReporterIonMassModeConstants)
        Options.ReporterIons.SetReporterIonMassMode(eReporterIonMassMode)
    End Sub

    <Obsolete("Use Options.ReporterIons.SetReporterIonMassMode")>
    Public Sub SetReporterIonMassMode(
      eReporterIonMassMode As clsReporterIons.eReporterIonMassModeConstants,
      mzToleranceDa As Double)

        Options.ReporterIons.SetReporterIonMassMode(eReporterIonMassMode, mzToleranceDa)

    End Sub

    ' ReSharper restore UnusedMember.Global

    Private Sub SetDefaultPeakLocValues(scanList As clsScanList)

        Dim parentIonIndex As Integer
        Dim scanIndexObserved As Integer

        Try
            For parentIonIndex = 0 To scanList.ParentIons.Count - 1
                With scanList.ParentIons(parentIonIndex)
                    scanIndexObserved = .SurveyScanIndex

                    With .SICStats
                        .ScanTypeForPeakIndices = clsScanList.eScanTypeConstants.SurveyScan
                        .PeakScanIndexStart = scanIndexObserved
                        .PeakScanIndexEnd = scanIndexObserved
                        .PeakScanIndexMax = scanIndexObserved
                    End With
                End With
            Next
        Catch ex As Exception
            LogErrors("SetDefaultPeakLocValues", "Error in clsMasic->SetDefaultPeakLocValues ", ex)
        End Try

    End Sub

    Private Sub SetLocalErrorCode(eNewErrorCode As eMasicErrorCodes)
        SetLocalErrorCode(eNewErrorCode, False)
    End Sub

    Private Sub SetLocalErrorCode(
      eNewErrorCode As eMasicErrorCodes,
      leaveExistingErrorCodeUnchanged As Boolean)

        If leaveExistingErrorCodeUnchanged AndAlso mLocalErrorCode <> eMasicErrorCodes.NoError Then
            ' An error code is already defined; do not change it
        Else
            mLocalErrorCode = eNewErrorCode

            If eNewErrorCode = eMasicErrorCodes.NoError Then
                If MyBase.ErrorCode = ProcessFilesErrorCodes.LocalizedError Then
                    MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.NoError)
                End If
            Else
                MyBase.SetBaseClassErrorCode(ProcessFilesErrorCodes.LocalizedError)
            End If
        End If

    End Sub

    ''' <summary>
    ''' Update subtask progress
    ''' </summary>
    ''' <param name="subtaskPercentComplete">Percent complete, between 0 and 100</param>
    Private Sub SetSubtaskProcessingStepPct(subtaskPercentComplete As Single)
        SetSubtaskProcessingStepPct(subtaskPercentComplete, False)
    End Sub

    ''' <summary>
    '''  Update subtask progress
    ''' </summary>
    ''' <param name="subtaskPercentComplete">Percent complete, between 0 and 100</param>
    ''' <param name="forceUpdate"></param>
    Private Sub SetSubtaskProcessingStepPct(subtaskPercentComplete As Single, forceUpdate As Boolean)
        Const MINIMUM_PROGRESS_UPDATE_INTERVAL_MILLISECONDS = 250

        Dim raiseEventNow As Boolean
        Static LastFileWriteTime As DateTime = DateTime.UtcNow

        If Math.Abs(subtaskPercentComplete) < Single.Epsilon Then
            AbortProcessing = False
            RaiseEvent ProgressResetKeypressAbort()
            raiseEventNow = True
        End If

        If Math.Abs(subtaskPercentComplete - mSubtaskProcessingStepPct) > Single.Epsilon Then
            raiseEventNow = True
            mSubtaskProcessingStepPct = subtaskPercentComplete
        End If

        If forceUpdate OrElse raiseEventNow OrElse
            DateTime.UtcNow.Subtract(LastFileWriteTime).TotalMilliseconds >= MINIMUM_PROGRESS_UPDATE_INTERVAL_MILLISECONDS Then
            LastFileWriteTime = DateTime.UtcNow

            UpdateOverallProgress()
            UpdateStatusFile()
            RaiseEvent ProgressSubtaskChanged()
        End If
    End Sub

    ''' <summary>
    ''' Update subtask progress and description
    ''' </summary>
    ''' <param name="subtaskPercentComplete">Percent complete, between 0 and 100</param>
    ''' <param name="message"></param>
    Private Sub SetSubtaskProcessingStepPct(subtaskPercentComplete As Single, message As String)
        mSubtaskDescription = message
        SetSubtaskProcessingStepPct(subtaskPercentComplete, True)
    End Sub

    Private Sub UpdateOverallProgress()
        UpdateOverallProgress(ProgressStepDescription)
    End Sub

    Private Sub UpdateOverallProgress(message As String)

        ' Update the processing progress, storing the value in mProgressPercentComplete

        'NewTask = 0
        'ReadDataFile = 1
        'SaveBPI = 2
        'CreateSICsAndFindPeaks = 3
        'FindSimilarParentIons = 4
        'SaveExtendedScanStatsFiles = 5
        'SaveSICStatsFlatFile = 6
        'CloseOpenFileHandles = 7
        'UpdateXMLFileWithNewOptimalPeakApexValues = 8
        'Cancelled = 99
        'Complete = 100

        Dim weightingFactors() As Single

        If Options.SkipMSMSProcessing Then
            ' Step                           0   1     2     3  4   5      6      7      8
            weightingFactors = New Single() {0, 0.97, 0.002, 0, 0, 0.001, 0.025, 0.001, 0.001}            ' The sum of these factors should be 1.00
        Else
            ' Step                           0   1      2      3     4     5      6      7      8
            weightingFactors = New Single() {0, 0.194, 0.003, 0.65, 0.09, 0.001, 0.006, 0.001, 0.055}     ' The sum of these factors should be 1.00
        End If

        Dim currentStep, index As Integer
        Dim overallPctCompleted As Single

        Try
            currentStep = mProcessingStep
            If currentStep >= weightingFactors.Length Then currentStep = weightingFactors.Length - 1

            overallPctCompleted = 0
            For index = 0 To currentStep - 1
                overallPctCompleted += weightingFactors(index) * 100
            Next

            overallPctCompleted += weightingFactors(currentStep) * mSubtaskProcessingStepPct

            mProgressPercentComplete = overallPctCompleted

        Catch ex As Exception
            LogErrors("UpdateOverallProgress", "Bug in UpdateOverallProgress", ex)
        End Try

        MyBase.UpdateProgress(message, mProgressPercentComplete)
    End Sub

    Private Sub UpdatePeakMemoryUsage()

        Dim memoryUsageMB As Single

        memoryUsageMB = GetProcessMemoryUsageMB()
        If memoryUsageMB > mProcessingStats.PeakMemoryUsageMB Then
            mProcessingStats.PeakMemoryUsageMB = memoryUsageMB
        End If

    End Sub

    Private Sub UpdateProcessingStep(eNewProcessingStep As eProcessingStepConstants, Optional forceStatusFileUpdate As Boolean = False)

        mProcessingStep = eNewProcessingStep
        UpdateStatusFile(forceStatusFileUpdate)

    End Sub

    Private Sub UpdateStatusFile(Optional forceUpdate As Boolean = False)

        Static LastFileWriteTime As DateTime = DateTime.UtcNow

        If forceUpdate OrElse DateTime.UtcNow.Subtract(LastFileWriteTime).TotalSeconds >= MINIMUM_STATUS_FILE_UPDATE_INTERVAL_SECONDS Then
            LastFileWriteTime = DateTime.UtcNow

            Try
                Dim tempPath = Path.Combine(GetAppDirectoryPath(), "Temp_" & Options.MASICStatusFilename)
                Dim statusFilePath = Path.Combine(GetAppDirectoryPath(), Options.MASICStatusFilename)

                Using writer = New Xml.XmlTextWriter(tempPath, Text.Encoding.UTF8)

                    writer.Formatting = Xml.Formatting.Indented
                    writer.Indentation = 2

                    writer.WriteStartDocument(True)
                    writer.WriteComment("MASIC processing status")

                    'Write the beginning of the "Root" element.
                    writer.WriteStartElement("Root")

                    writer.WriteStartElement("General")
                    writer.WriteElementString("LastUpdate", DateTime.Now.ToString())
                    writer.WriteElementString("ProcessingStep", mProcessingStep.ToString())
                    writer.WriteElementString("Progress", StringUtilities.DblToString(mProgressPercentComplete, 2))
                    writer.WriteElementString("Error", GetErrorMessage())
                    writer.WriteEndElement()

                    writer.WriteStartElement("Statistics")
                    writer.WriteElementString("FreeMemoryMB", StringUtilities.DblToString(GetFreeMemoryMB(), 1))
                    writer.WriteElementString("MemoryUsageMB", StringUtilities.DblToString(GetProcessMemoryUsageMB, 1))
                    writer.WriteElementString("PeakMemoryUsageMB", StringUtilities.DblToString(mProcessingStats.PeakMemoryUsageMB, 1))

                    With mProcessingStats
                        writer.WriteElementString("CacheEventCount", .CacheEventCount.ToString())
                        writer.WriteElementString("UnCacheEventCount", .UnCacheEventCount.ToString())
                    End With

                    writer.WriteElementString("ProcessingTimeSec", StringUtilities.DblToString(GetTotalProcessingTimeSec(), 2))
                    writer.WriteEndElement()

                    writer.WriteEndElement()  'End the "Root" element.
                    writer.WriteEndDocument() 'End the document

                End Using

                'Copy the temporary file to the real one
                File.Copy(tempPath, statusFilePath, True)
                File.Delete(tempPath)

            Catch ex As Exception
                ' Ignore any errors
            End Try

        End If

    End Sub

#Region "Event Handlers"

    Private Sub MessageEventHandler(message As String)
        LogMessage(message)
    End Sub

    Private Sub ErrorEventHandler(message As String, ex As Exception)
        LogErrors(String.Empty, message, ex)
    End Sub

    Private Sub WarningEventHandler(message As String)
        LogMessage(message, MessageTypeConstants.Warning)
    End Sub

    ''' <summary>
    ''' Update progress
    ''' </summary>
    ''' <param name="progressMessage">Progress message (can be empty)</param>
    ''' <param name="percentComplete">Value between 0 and 100</param>
    Private Sub ProgressUpdateHandler(progressMessage As String, percentComplete As Single)
        If String.IsNullOrEmpty(progressMessage) Then
            SetSubtaskProcessingStepPct(percentComplete)
        Else
            SetSubtaskProcessingStepPct(percentComplete, progressMessage)
        End If

    End Sub

    Private Sub UpdatedCacheStatsEventHandler(cacheEventCount As Integer, unCacheEventCount As Integer)
        mProcessingStats.CacheEventCount = cacheEventCount
        mProcessingStats.UnCacheEventCount = unCacheEventCount
    End Sub

    Private Sub UpdateBaseClassErrorCodeEventHandler(eErrorCode As ProcessFilesErrorCodes)
        SetBaseClassErrorCode(eErrorCode)
    End Sub

    Private Sub UpdateErrorCodeEventHandler(eErrorCode As eMasicErrorCodes, leaveExistingErrorCodeUnchanged As Boolean)
        SetLocalErrorCode(eErrorCode, leaveExistingErrorCodeUnchanged)
    End Sub

    Private Sub UpdateMemoryUsageEventHandler()
        ' Record the current memory usage
        Dim memoryUsageMB = GetProcessMemoryUsageMB()
        If memoryUsageMB > mProcessingStats.MemoryUsageMBDuringLoad Then
            mProcessingStats.MemoryUsageMBDuringLoad = memoryUsageMB
        End If

    End Sub

#End Region

End Class
