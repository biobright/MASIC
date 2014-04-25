Option Strict On

' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Program started October 11, 2003
' Copyright 2005, Battelle Memorial Institute.  All Rights Reserved.

' E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
' Website: http://panomics.pnnl.gov/ or http://www.sysbio.org/resources/staff/
' -------------------------------------------------------------------------------
' 
' Licensed under the Apache License, Version 2.0; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at 
' http://www.apache.org/licenses/LICENSE-2.0
'
' Notice: This computer software was prepared by Battelle Memorial Institute, 
' hereinafter the Contractor, under Contract No. DE-AC05-76RL0 1830 with the 
' Department of Energy (DOE).  All rights in the computer software are reserved 
' by DOE on behalf of the United States Government and the Contractor as 
' provided in the Contract.  NEITHER THE GOVERNMENT NOR THE CONTRACTOR MAKES ANY 
' WARRANTY, EXPRESS OR IMPLIED, OR ASSUMES ANY LIABILITY FOR THE USE OF THIS 
' SOFTWARE.  This notice including this sentence must appear on any copies of 
' this computer software.

Imports System.Windows.Forms
Imports MASIC.clsMASIC
Imports MASICPeakFinder.clsMASICPeakFinder

Public Class frmMain

    Public Sub New()
        MyBase.New()

        'This call is required by the Windows Form Designer.
        InitializeComponent()

        'Add any initialization after the InitializeComponent() call
        InitializeControls()
    End Sub

#Region "Constants and Enums"

	Private Const XML_SETTINGS_FILE_NAME As String = "MASICParameters.xml"

	Private Const CUSTOM_SIC_VALUES_DATATABLE As String = "PeakMatchingThresholds"

	Private Const COL_NAME_MZ As String = "MZ"
	Private Const COL_NAME_MZ_TOLERANCE As String = "MZToleranceDa"
	Private Const COL_NAME_SCAN_CENTER As String = "Scan_Center"
	Private Const COL_NAME_SCAN_TOLERANCE As String = "Scan_Tolerance"
	Private Const COL_NAME_SCAN_COMMENT As String = "Scan_Comment"
	Private Const COL_NAME_CUSTOM_SIC_VALUE_ROW_ID As String = "UniqueRowID"

#End Region

#Region "Structures"

	Private Structure udtCustomSICEntryType
		Public MZ As Double
		Public ScanCenter As Single
		Public Comment As String
	End Structure

#End Region

#Region "Classwide Variables"

	Private mCustomSICValuesDataset As System.Data.DataSet

	Private mDefaultCustomSICList() As udtCustomSICEntryType
	Private mWorking As Boolean

	Private mXmlSettingsFilePath As String
	Private mPreferredInputFileExtension As String

	Private mUseFinniganXRawAccessorFunctions As Boolean

	Private mCacheOptions As clsSpectraCache.udtSpectrumCacheOptionsType

	Private mSuppressNoParentIonsError As Boolean
	Private mCompressMSSpectraData As Boolean
	Private mCompressMSMSSpectraData As Boolean

	Private mCompressToleranceDivisorForDa As Double
	Private mCompressToleranceDivisorForPPM As Double

	Private mHeightAdjustForce As Integer
	Private mHeightAdjustTime As DateTime

	Private WithEvents mMasic As clsMASIC
	Private mProgressForm As ProgressFormNET.frmProgress
#End Region

#Region "Procedures"

	Private Sub AddCustomSICRow(ByVal dblMZ As Double, ByVal sngScanOrAcqTimeCenter As Single, ByVal strComment As String, Optional ByRef blnExistingRowFound As Boolean = False)
		Dim dblDefaultMZTolerance As Double
		Dim sngDefaultScanOrAcqTimeTolerance As Single

		GetCurrentCustomSICTolerances(dblDefaultMZTolerance, sngDefaultScanOrAcqTimeTolerance)

		AddCustomSICRow(dblMZ, dblDefaultMZTolerance, sngScanOrAcqTimeCenter, sngDefaultScanOrAcqTimeTolerance, String.Empty, blnExistingRowFound)
	End Sub

	Private Sub AddCustomSICRow(ByVal dblMZ As Double, ByVal dblMZToleranceDa As Double, ByVal sngScanOrAcqTimeCenter As Single, ByVal sngScanOrAcqTimeTolerance As Single, ByVal strComment As String, Optional ByRef blnExistingRowFound As Boolean = False)
		Dim myDataRow As DataRow

		With mCustomSICValuesDataset.Tables(CUSTOM_SIC_VALUES_DATATABLE)

			For Each myDataRow In .Rows
				With myDataRow
					If CDbl(.Item(0)) = dblMZ And CSng(.Item(1)) = sngScanOrAcqTimeCenter Then
						blnExistingRowFound = True
						Exit For
					End If
				End With
			Next myDataRow

			If strComment Is Nothing Then strComment = String.Empty

			If Not blnExistingRowFound Then
				myDataRow = .NewRow
				myDataRow(0) = Math.Round(dblMZ, 4)
				myDataRow(1) = Math.Round(dblMZToleranceDa, 4)
				myDataRow(2) = Math.Round(sngScanOrAcqTimeCenter, 6)
				myDataRow(3) = Math.Round(sngScanOrAcqTimeTolerance, 6)
				myDataRow(4) = strComment
				.Rows.Add(myDataRow)
			End If
		End With

	End Sub

	Private Sub AutoPopulateCustomSICValues(ByVal blnConfirmReplaceExistingResults As Boolean)

		Dim intIndex As Integer

		Dim dblDefaultMZTolerance As Double
		Dim sngDefaultScanOrAcqTimeTolerance As Single

		GetCurrentCustomSICTolerances(dblDefaultMZTolerance, sngDefaultScanOrAcqTimeTolerance)
		If sngDefaultScanOrAcqTimeTolerance > 1 Then
			sngDefaultScanOrAcqTimeTolerance = 0.6
		End If

		If ClearCustomSICList(blnConfirmReplaceExistingResults) Then
			' The default values use relative times, so make sure that mode is enabled
			SetCustomSICToleranceType(clsMASIC.eCustomSICScanTypeConstants.Relative)

			txtCustomSICScanOrAcqTimeTolerance.Text = sngDefaultScanOrAcqTimeTolerance.ToString

			For intIndex = 0 To mDefaultCustomSICList.Length - 1
				With mDefaultCustomSICList(intIndex)
					AddCustomSICRow(.MZ, dblDefaultMZTolerance, .ScanCenter, sngDefaultScanOrAcqTimeTolerance, .Comment)
				End With
			Next intIndex

		End If

	End Sub

	Private Sub CatchUnrequestedHeightChange()
		Static blnUpdating As Boolean

		If Not blnUpdating Then
			If mHeightAdjustForce <> 0 AndAlso System.DateTime.UtcNow.Subtract(mHeightAdjustTime).TotalSeconds <= 5 Then
				Try
					blnUpdating = True
					Me.Height = mHeightAdjustForce
					mHeightAdjustForce = 0
					mHeightAdjustTime = #1/1/1900#
				Catch ex As Exception
				Finally
					blnUpdating = False
				End Try
			End If
		End If
	End Sub

	Private Sub AutoToggleReporterIonStatsEnabled()
		If cboReporterIonMassMode.SelectedIndex = clsMASIC.eReporterIonMassModeConstants.CustomOrNone Then
			If chkReporterIonStatsEnabled.Checked Then
				chkReporterIonStatsEnabled.Checked = False
			End If
		Else
			If Not chkReporterIonStatsEnabled.Checked Then
				chkReporterIonStatsEnabled.Checked = True
			End If
		End If
	End Sub

	Private Sub AutoToggleReporterIonStatsMode()
		If chkReporterIonStatsEnabled.Checked Then
			If cboReporterIonMassMode.SelectedIndex = clsMASIC.eReporterIonMassModeConstants.CustomOrNone Then
				cboReporterIonMassMode.SelectedIndex = clsMASIC.eReporterIonMassModeConstants.ITraqFourMZ
			End If
		Else
			If cboReporterIonMassMode.SelectedIndex <> clsMASIC.eReporterIonMassModeConstants.CustomOrNone Then
				cboReporterIonMassMode.SelectedIndex = clsMASIC.eReporterIonMassModeConstants.CustomOrNone
			End If
		End If
	End Sub

	Private Sub ClearAllRangeFilters()
		txtScanStart.Text = "0"
		txtScanEnd.Text = "0"
		txtTimeStart.Text = "0"
		txtTimeEnd.Text = "0"
	End Sub

	Private Function ClearCustomSICList(ByVal blnConfirmReplaceExistingResults As Boolean) As Boolean
		' Returns true if the CUSTOM_SIC_VALUES_DATATABLE is empty or if it was cleared
		' Returns false if the user is queried about clearing and they do not click Yes

		Dim eResult As System.Windows.Forms.DialogResult
		Dim blnSuccess As Boolean

		blnSuccess = False
		With mCustomSICValuesDataset.Tables(CUSTOM_SIC_VALUES_DATATABLE)
			If .Rows.Count > 0 Then
				If blnConfirmReplaceExistingResults Then
					eResult = Windows.Forms.MessageBox.Show("Are you sure you want to clear the Custom SIC list?", "Clear Custom SICs", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
				End If

				If eResult = DialogResult.Yes OrElse Not blnConfirmReplaceExistingResults Then
					.Rows.Clear()
					blnSuccess = True
				End If
			Else
				blnSuccess = True
			End If
		End With

		Return blnSuccess
	End Function

	Private Function ConfirmPaths() As Boolean
		If txtInputFilePath.TextLength = 0 Then
			Windows.Forms.MessageBox.Show("Please define an input file path", "Missing Value", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
			txtInputFilePath.Focus()
			Return False
		ElseIf txtOutputFolderPath.TextLength = 0 Then
			Windows.Forms.MessageBox.Show("Please define an output folder path", "Missing Value", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
			txtOutputFolderPath.Focus()
			Return False
		Else
			Return True
		End If
	End Function

	Private Sub DefineDefaultCustomSICList()

		ReDim mDefaultCustomSICList(8)

		With mDefaultCustomSICList(0)
			.MZ = 824.47422
			.ScanCenter = 0.176
			.Comment = "Pep-09"
		End With

		With mDefaultCustomSICList(1)
			.MZ = 412.74102
			.ScanCenter = 0.176
			.Comment = "Pep-09"
		End With

		With mDefaultCustomSICList(2)
			.MZ = 484.28137
			.ScanCenter = 0.092
			.Comment = "Pep-11"
		End With

		With mDefaultCustomSICList(3)
			.MZ = 459.27687
			.ScanCenter = 0.368
			.Comment = "Pep-14"
		End With

		With mDefaultCustomSICList(4)
			.MZ = 740.01082
			.ScanCenter = 0.574
			.Comment = "Pep-16"
		End With

		With mDefaultCustomSICList(5)
			.MZ = 762.51852
			.ScanCenter = 0.642
			.Comment = "Pep-26"
		End With

		With mDefaultCustomSICList(6)
			.MZ = 657.42992
			.ScanCenter = 0.192
			.Comment = "Pep-16_Partial"
		End With

		With mDefaultCustomSICList(7)
			.MZ = 900.59222
			.ScanCenter = 0.4
			.Comment = "Pep-26_PartialA"
		End With

		With mDefaultCustomSICList(8)
			.MZ = 640.43972
			.ScanCenter = 0.4
			.Comment = "Pep-26_PartialB"
		End With

	End Sub

	Private Sub DefineOverviewText()
		Dim Msg As String

		Msg = String.Empty
		Msg &= "When Export All Spectra Data Points is enabled, a separate file is created containing the raw data points (scan number, m/z, and intensity), using the specified file format. "
		Msg &= "If Export MS/MS Spectra is enabled, then the fragmentation spectra are included, in addition to the survey scan spectra (MS1 scans). "
		Msg &= "If MS/MS spectra are not included, then one can optionally renumber the survey scan spectra so that they increase in steps of 1, regardless of the number of MS/MS scans between each survey scan. "
		Msg &= "The Minimum Intensity and Maximum Ion Count options allow you to limit the number of data points exported for each spectrum."

		lblRawDataExportOverview.Text = Msg

		Msg = String.Empty
		Msg &= "These options control how the selected ion chromatogram (SIC) is created for each parent ion mass or custom SIC search mass. "
		Msg &= "The data in the survey scan spectra (MS1 scans) are searched +/- the SIC Tolerance, looking forward and backward in time until "
		Msg &= "the intensity of the matching data 1) falls below the Intensity Threshold Fraction Max Peak value, 2) falls below the Intensity "
		Msg &= "Threshold Absolute Minimum, or 3) spans more than the Maximum Peak Width forward or backward limits defined."

		lblSICOptionsOverview.Text = Msg


		Msg = String.Empty
		Msg &= "When processing Thermo-Finnigan MRM data files, a file named _MRMSettings.txt will be created listing the "
		Msg &= "parent and daughter m/z values monitored via SRM. "
		Msg &= "You can optionally export detailed MRM intensity data using these options:"
		lblMRMInfo.Text = Msg


		Msg = String.Empty
		Msg &= "Select a comma or tab delimited file to read custom SIC search values from, "
		Msg &= "or define them in the Custom SIC Values table below.  If using the file, "
		Msg &= "allowed column names are: " & clsMASIC.GetCustomMZFileColumnHeaders() & ".  "
		Msg &= "Note: use " & _
		  clsMASIC.CUSTOM_SIC_COLUMN_SCAN_TIME & " and " & _
		  clsMASIC.CUSTOM_SIC_COLUMN_TIME_TOLERANCE & " only when specifying "

		Msg &= "acquisition time-based values.  When doing so, do not include " & _
		  clsMASIC.CUSTOM_SIC_COLUMN_SCAN_CENTER & " and " & _
		  clsMASIC.CUSTOM_SIC_COLUMN_SCAN_TOLERNACE & "."

		txtCustomSICFileDescription.Text = Msg

	End Sub

	Private Sub DeleteCachedSpectrumFiles()
		Dim objSpectraCache As New clsSpectraCache
		objSpectraCache.DeleteSpectrumCacheFiles()
		objSpectraCache = Nothing
	End Sub

	Private Sub EnableDisableControls()
		Dim blnCreateSICsAndRawData As Boolean
		Dim blnExportRawDataOnly As Boolean
		Dim blnMSMSProcessingEnabled As Boolean

		Dim blnRawExportEnabled As Boolean

		blnCreateSICsAndRawData = Not chkSkipSICAndRawDataProcessing.Checked
		blnMSMSProcessingEnabled = Not chkSkipMSMSProcessing.Checked
		blnExportRawDataOnly = chkExportRawDataOnly.Checked And chkExportRawSpectraData.Checked

		chkSkipMSMSProcessing.Enabled = blnCreateSICsAndRawData
		chkExportRawDataOnly.Enabled = blnCreateSICsAndRawData And chkExportRawSpectraData.Checked

		fraExportAllSpectraDataPoints.Enabled = blnCreateSICsAndRawData

		fraSICNoiseThresholds.Enabled = blnCreateSICsAndRawData And Not blnExportRawDataOnly
		fraPeakFindingOptions.Enabled = fraSICNoiseThresholds.Enabled
		fraSmoothingOptions.Enabled = fraSICNoiseThresholds.Enabled
		fraSICSearchThresholds.Enabled = fraSICNoiseThresholds.Enabled

		fraMassSpectraNoiseThresholds.Enabled = blnCreateSICsAndRawData

		fraBinningIntensityOptions.Enabled = blnCreateSICsAndRawData And blnMSMSProcessingEnabled And Not blnExportRawDataOnly
		fraBinningMZOptions.Enabled = fraBinningIntensityOptions.Enabled
		fraSpectrumSimilarityOptions.Enabled = fraBinningIntensityOptions.Enabled

		fraCustomSICControls.Enabled = blnCreateSICsAndRawData And Not blnExportRawDataOnly
		dgCustomSICValues.Enabled = blnCreateSICsAndRawData And Not blnExportRawDataOnly

		blnRawExportEnabled = chkExportRawSpectraData.Checked

		cboExportRawDataFileFormat.Enabled = blnRawExportEnabled
		chkExportRawDataIncludeMSMS.Enabled = blnRawExportEnabled

		If chkExportRawDataIncludeMSMS.Checked Then
			chkExportRawDataRenumberScans.Enabled = False
		Else
			chkExportRawDataRenumberScans.Enabled = True And blnRawExportEnabled
		End If

		txtExportRawDataSignalToNoiseRatioMinimum.Enabled = blnRawExportEnabled
		txtExportRawDataMaxIonCountPerScan.Enabled = blnRawExportEnabled
		txtExportRawDataIntensityMinimum.Enabled = blnRawExportEnabled


		If cboSICNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.AbsoluteThreshold Then
			txtSICNoiseThresholdIntensity.Enabled = True
			txtSICNoiseFractionLowIntensityDataToAverage.Enabled = False
			txtSICNoiseMinimumSignalToNoiseRatio.Enabled = False
		ElseIf cboSICNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMeanByAbundance Or _
		 cboSICNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMeanByCount Or _
		 cboSICNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMedianByAbundance Then
			txtSICNoiseThresholdIntensity.Enabled = False
			txtSICNoiseFractionLowIntensityDataToAverage.Enabled = True
			txtSICNoiseMinimumSignalToNoiseRatio.Enabled = True
		Else
			' Unknown mode; disable both
			txtSICNoiseThresholdIntensity.Enabled = False
			txtSICNoiseFractionLowIntensityDataToAverage.Enabled = False
			txtSICNoiseMinimumSignalToNoiseRatio.Enabled = False
		End If

		txtButterworthSamplingFrequency.Enabled = optUseButterworthSmooth.Checked
		txtSavitzkyGolayFilterOrder.Enabled = optUseSavitzkyGolaySmooth.Checked

		If cboMassSpectraNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.AbsoluteThreshold Then
			txtMassSpectraNoiseThresholdIntensity.Enabled = True
			txtMassSpectraNoiseFractionLowIntensityDataToAverage.Enabled = False
			txtMassSpectraNoiseMinimumSignalToNoiseRatio.Enabled = False
		ElseIf cboMassSpectraNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMeanByAbundance Or
		  cboMassSpectraNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMeanByCount Or
		  cboMassSpectraNoiseThresholdMode.SelectedIndex = eNoiseThresholdModes.TrimmedMedianByAbundance Then
			txtMassSpectraNoiseThresholdIntensity.Enabled = False
			txtMassSpectraNoiseFractionLowIntensityDataToAverage.Enabled = True
			txtMassSpectraNoiseMinimumSignalToNoiseRatio.Enabled = True
		Else
			' Unknown mode; disable both
			txtMassSpectraNoiseThresholdIntensity.Enabled = False
			txtMassSpectraNoiseFractionLowIntensityDataToAverage.Enabled = False
			txtMassSpectraNoiseMinimumSignalToNoiseRatio.Enabled = False
		End If

		chkSaveExtendedStatsFileIncludeFilterText.Enabled = chkSaveExtendedStatsFile.Checked
		chkSaveExtendedStatsFileIncludeStatusLog.Enabled = chkSaveExtendedStatsFile.Checked
		txtStatusLogKeyNameFilterList.Enabled = chkSaveExtendedStatsFile.Checked And chkSaveExtendedStatsFileIncludeStatusLog.Checked

		chkConsolidateConstantExtendedHeaderValues.Enabled = chkSaveExtendedStatsFile.Checked

		EnableDisableCustomSICValueGrid()

	End Sub

	Private Sub EnableDisableCustomSICValueGrid()
		Dim blnEnableGrid As Boolean

		If txtCustomSICFileName.TextLength > 0 Then
			blnEnableGrid = False
			dgCustomSICValues.CaptionText = "Custom SIC Values will be read from the file defined above"
		Else
			blnEnableGrid = True
			dgCustomSICValues.CaptionText = "Custom SIC Values"
		End If

		cmdPasteCustomSICList.Enabled = blnEnableGrid
		cmdCustomSICValuesPopulate.Enabled = blnEnableGrid
		cmdClearCustomSICList.Enabled = blnEnableGrid
		dgCustomSICValues.Enabled = blnEnableGrid

	End Sub

	Private Sub frmMain_Resize(ByVal sender As Object, ByVal e As System.EventArgs) Handles MyBase.Resize
		CatchUnrequestedHeightChange()
	End Sub

	Private Sub GetCurrentCustomSICTolerances(ByRef dblDefaultMZTolerance As Double, ByRef sngDefaultScanOrAcqTimeTolerance As Single)

		Try
			dblDefaultMZTolerance = Double.Parse(txtSICTolerance.Text)

			If optSICTolerancePPM.Checked Then
				dblDefaultMZTolerance = clsMASIC.PPMToMass(dblDefaultMZTolerance, 1000)
			End If

		Catch ex As Exception
			dblDefaultMZTolerance = 0.6
		End Try

		Try
			sngDefaultScanOrAcqTimeTolerance = Single.Parse(txtCustomSICScanOrAcqTimeTolerance.Text)
		Catch ex As Exception
			sngDefaultScanOrAcqTimeTolerance = 0
		End Try
	End Sub

	Private Function GetCustomSICScanToleranceType() As clsMASIC.eCustomSICScanTypeConstants

		If optCustomSICScanToleranceAbsolute.Checked Then
			Return clsMASIC.eCustomSICScanTypeConstants.Absolute

		ElseIf optCustomSICScanToleranceRelative.Checked Then
			Return clsMASIC.eCustomSICScanTypeConstants.Relative

		ElseIf optCustomSICScanToleranceAcqTime.Checked Then
			Return clsMASIC.eCustomSICScanTypeConstants.AcquisitionTime

		Else
			' Assume absolute
			Return clsMASIC.eCustomSICScanTypeConstants.Absolute
		End If

	End Function

	Private Function GetSettingsFilePath() As String
		Return clsProcessFilesBaseClass.GetSettingsFilePathLocal("MASIC", XML_SETTINGS_FILE_NAME)
	End Function

	Private Sub IniFileLoadOptions(ByVal blnUpdateIOPaths As Boolean)
		' Prompts the user to select a file to load the options from

		Dim strFilePath As String

		Dim objOpenFile As New System.Windows.Forms.OpenFileDialog

		strFilePath = mXmlSettingsFilePath

		With objOpenFile
			.AddExtension = True
			.CheckFileExists = True
			.CheckPathExists = True
			.DefaultExt = ".xml"
			.DereferenceLinks = True
			.Multiselect = False
			.ValidateNames = True

			.Filter = "Settings files (*.xml)|*.xml|All files (*.*)|*.*"

			.FilterIndex = 1

			If strFilePath.Length > 0 Then
				Try
					.InitialDirectory = System.IO.Directory.GetParent(strFilePath).ToString
				Catch
					.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
				End Try
			Else
				.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
			End If

			If System.IO.File.Exists(strFilePath) Then
				.FileName = System.IO.Path.GetFileName(strFilePath)
			End If

			.Title = "Specify file to load options from"

			.ShowDialog()
			If .FileName.Length > 0 Then
				mXmlSettingsFilePath = .FileName

				IniFileLoadOptions(mXmlSettingsFilePath, blnUpdateIOPaths)
			End If
		End With

	End Sub

	Private Sub IniFileLoadOptions(ByVal strFilePath As String, ByVal blnUpdateIOPaths As Boolean)
		' Loads options from the given file

		Dim objXmlFile As New XmlSettingsFileAccessor
		Dim objMasic As clsMASIC

		Try

			' Utilize clsMASIC's built-in LoadParameters function, then call ResetToDefaults
			objMasic = New clsMASIC

			objMasic.LoadParameterFileSettings(strFilePath)

			ResetToDefaults(False, objMasic)

			objMasic = Nothing

			' Sleep for 100 msec, just to be safe
			System.Threading.Thread.Sleep(100)

			' Now load some custom options that aren't loaded by clsMASIC
			With objXmlFile
				' Pass True to .LoadSettings() to turn off case sensitive matching
				.LoadSettings(strFilePath, False)

				Try
					txtDatasetLookupFilePath.Text = .GetParam(clsMASIC.XML_SECTION_DATABASE_SETTINGS, "DatasetLookupFilePath", txtDatasetLookupFilePath.Text)
					Try
						If Not System.IO.File.Exists(txtDatasetLookupFilePath.Text) Then
							txtDatasetLookupFilePath.Text = String.Empty
						End If
					Catch ex As Exception
						' Ignore any errors here
					End Try

					If blnUpdateIOPaths Then
						txtInputFilePath.Text = .GetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "InputFilePath", txtInputFilePath.Text)
					End If

					Me.Width = .GetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "WindowWidth", Me.Width)
					Me.Height = .GetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "WindowHeight", Me.Height)

					If blnUpdateIOPaths Then
						txtOutputFolderPath.Text = .GetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "LastDirectory", txtOutputFolderPath.Text)
					End If

					If txtOutputFolderPath.TextLength = 0 Then
						txtOutputFolderPath.Text = clsProcessFilesBaseClass.GetAppFolderPath()
					End If

					mPreferredInputFileExtension = .GetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "PreferredInputFileExtension", mPreferredInputFileExtension)

				Catch ex As Exception
					System.Windows.Forms.MessageBox.Show("Invalid parameter in settings file: " & System.IO.Path.GetFileName(strFilePath), "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
				End Try
			End With

			objXmlFile = Nothing

		Catch ex As Exception
			System.Windows.Forms.MessageBox.Show("Error loading settings from file: " & strFilePath & "; " & ControlChars.NewLine & _
			 ex.Message & ";" & ControlChars.NewLine, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
		End Try

	End Sub

	Private Sub IniFileSaveDefaultOptions()
		Dim eResponse As System.Windows.Forms.DialogResult

		eResponse = MessageBox.Show("Save the current options as defaults?", "Save Defaults", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
		If eResponse = DialogResult.Yes Then
			IniFileSaveOptions(GetSettingsFilePath(), False)
		End If
	End Sub

	Private Sub IniFileSaveOptions()
		' Prompts the user to select a file to load the options from

		Dim strFilePath As String

		Dim objSaveFile As New System.Windows.Forms.SaveFileDialog

		strFilePath = mXmlSettingsFilePath

		With objSaveFile
			.AddExtension = True
			.CheckFileExists = False
			.CheckPathExists = True
			.DefaultExt = ".xml"
			.DereferenceLinks = True
			.OverwritePrompt = True
			.ValidateNames = True

			.Filter = "Settings files (*.xml)|*.xml|All files (*.*)|*.*"

			.FilterIndex = 1

			If strFilePath.Length > 0 Then
				Try
					.InitialDirectory = System.IO.Directory.GetParent(strFilePath).ToString
				Catch
					.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
				End Try
			Else
				.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
			End If

			If System.IO.File.Exists(strFilePath) Then
				.FileName = System.IO.Path.GetFileName(strFilePath)
			End If

			.Title = "Specify file to save options to"

			.ShowDialog()
			If .FileName.Length > 0 Then
				mXmlSettingsFilePath = .FileName

				IniFileSaveOptions(mXmlSettingsFilePath, False)
			End If
		End With

	End Sub

	Private Sub IniFileSaveOptions(ByVal strFilePath As String, Optional ByVal blnSaveWindowDimensionsOnly As Boolean = False)
		Dim objXmlFile As New XmlSettingsFileAccessor

		Dim objMasic As clsMASIC

		Try
			If Not blnSaveWindowDimensionsOnly Then
				objMasic = New clsMASIC

				UpdateMasicSettings(objMasic)

				objMasic.SaveParameterFileSettings(strFilePath)

				objMasic = Nothing

				' Sleep for 100 msec, just to be safe
				System.Threading.Thread.Sleep(100)
			End If

			With objXmlFile
				' Pass True to .LoadSettings() here so that newly made Xml files will have the correct capitalization
				.LoadSettings(strFilePath, True)

				Try
					If Not blnSaveWindowDimensionsOnly Then
						Try
							If System.IO.File.Exists(txtDatasetLookupFilePath.Text) Then
								.SetParam(clsMASIC.XML_SECTION_DATABASE_SETTINGS, "DatasetLookupFilePath", txtDatasetLookupFilePath.Text)
							End If
						Catch ex As Exception
							' Ignore any errors here
						End Try

						.SetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "InputFilePath", txtInputFilePath.Text)
					End If

					.SetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "LastDirectory", txtOutputFolderPath.Text)
					.SetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "PreferredInputFileExtension", mPreferredInputFileExtension)

					.SetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "WindowWidth", Me.Width)
					.SetParam(clsMASIC.XML_SECTION_IMPORT_OPTIONS, "WindowHeight", Me.Height)

				Catch ex As Exception
					System.Windows.Forms.MessageBox.Show("Error storing parameter in settings file: " & System.IO.Path.GetFileName(strFilePath), "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
				End Try

				.SaveSettings()
			End With
		Catch ex As Exception
			System.Windows.Forms.MessageBox.Show("Error saving settings to file: " & strFilePath, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
		End Try


	End Sub

	Private Sub InitializeControls()

		DefineDefaultCustomSICList()

		PopulateComboBoxes()

		InitializeCustomSICDataGrid()

		DefineOverviewText()

		mXmlSettingsFilePath = GetSettingsFilePath()
		clsProcessFilesBaseClass.CreateSettingsFileIfMissing(mXmlSettingsFilePath)

		mPreferredInputFileExtension = ".Raw"

		mHeightAdjustForce = 0
		mHeightAdjustTime = #1/1/1900#

		IniFileLoadOptions(mXmlSettingsFilePath, True)
		SetToolTips()

	End Sub

	Private Sub InitializeCustomSICDataGrid()

		' Make the Peak Matching Thresholds datatable
		Dim dtCustomSICValues As System.Data.DataTable = New System.Data.DataTable(CUSTOM_SIC_VALUES_DATATABLE)

		' Add the columns to the datatable
		SharedVBNetRoutines.ADONetRoutines.AppendColumnDoubleToTable(dtCustomSICValues, COL_NAME_MZ)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnDoubleToTable(dtCustomSICValues, COL_NAME_MZ_TOLERANCE)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnDoubleToTable(dtCustomSICValues, COL_NAME_SCAN_CENTER)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnDoubleToTable(dtCustomSICValues, COL_NAME_SCAN_TOLERANCE)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnStringToTable(dtCustomSICValues, COL_NAME_SCAN_COMMENT, String.Empty)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnIntegerToTable(dtCustomSICValues, COL_NAME_CUSTOM_SIC_VALUE_ROW_ID, 0, True, True)

		With dtCustomSICValues
			Dim PrimaryKeyColumn As System.Data.DataColumn() = New DataColumn() {.Columns(COL_NAME_CUSTOM_SIC_VALUE_ROW_ID)}
			.PrimaryKey = PrimaryKeyColumn
		End With

		' Instantiate the dataset
		mCustomSICValuesDataset = New System.Data.DataSet(CUSTOM_SIC_VALUES_DATATABLE)

		' Add the new DataTable to the DataSet.
		mCustomSICValuesDataset.Tables.Add(dtCustomSICValues)

		' Bind the DataSet to the DataGrid
		With dgCustomSICValues
			.DataSource = mCustomSICValuesDataset
			.DataMember = CUSTOM_SIC_VALUES_DATATABLE
		End With

		' Update the grid's table style
		UpdateCustomSICDataGridTableStyle()

		' Populate the table
		AutoPopulateCustomSICValues(False)

	End Sub

	Private Sub PasteCustomSICValues(ByVal blnClearList As Boolean)
		Dim objData As System.Windows.Forms.IDataObject

		Dim strData As String
		Dim strLines() As String
		Dim strColumns() As String

		Dim dblMZ As Double
		Dim sngScanOrAcqTime As Single
		Dim strComment As String = String.Empty

		Dim strMessage As String

		Dim blnExistingRowFound As Boolean
		Dim intRowsAlreadyPresent As Integer
		Dim intRowsSkipped As Integer

		Dim intLineIndex As Integer

		Dim LineDelimiters() As Char = New Char() {ControlChars.Cr, ControlChars.Lf}
		Dim ColumnDelimiters() As Char = New Char() {ControlChars.Tab, ","c}

		Dim dblMZToleranceDa As Double
		Dim sngScanOrAcqTimeTolerance As Single

		Dim dblDefaultMZTolerance As Double
		Dim sngDefaultScanOrAcqTimeTolerance As Single

		' Examine the clipboard contents
		objData = Clipboard.GetDataObject()

		If Not objData Is Nothing Then
			If objData.GetDataPresent(System.Windows.Forms.DataFormats.StringFormat, True) Then
				strData = CType(objData.GetData(System.Windows.Forms.DataFormats.StringFormat, True), String)

				' Split strData on carriage return or line feed characters
				' Lines that end in CrLf will give two separate lines; one with with the text, and one blank; that's OK
				strLines = strData.Split(LineDelimiters, 50000)

				If strLines.Length > 0 Then
					GetCurrentCustomSICTolerances(dblDefaultMZTolerance, sngDefaultScanOrAcqTimeTolerance)

					If blnClearList Then
						If Not ClearCustomSICList(True) Then Return
					End If

					intRowsAlreadyPresent = 0
					intRowsSkipped = 0

					For intLineIndex = 0 To strLines.Length - 1
						If Not strLines(intLineIndex) Is Nothing AndAlso strLines(intLineIndex).Length > 0 Then
							strColumns = strLines(intLineIndex).Split(ColumnDelimiters, 5)
							If strColumns.Length >= 2 Then
								Try
									dblMZ = 0
									dblMZToleranceDa = dblDefaultMZTolerance
									sngScanOrAcqTimeTolerance = sngDefaultScanOrAcqTimeTolerance

									If strColumns.Length = 2 Then
										' Assume pasted data is m/z and scan
										dblMZ = Double.Parse(strColumns(0))
										sngScanOrAcqTime = Single.Parse(strColumns(1))

									ElseIf strColumns.Length >= 3 AndAlso strColumns(2).Length > 0 AndAlso Not SharedVBNetRoutines.VBNetRoutines.IsNumber(strColumns(2).Chars(0)) Then
										' Assume pasted data is m/z, scan, and comment
										dblMZ = Double.Parse(strColumns(0))
										sngScanOrAcqTime = Single.Parse(strColumns(1))
										strComment = strColumns(2)

									ElseIf strColumns.Length > 2 Then
										' Assume pasted data is m/z, m/z tolerance, scan, scan tolerance, and comment
										dblMZ = Double.Parse(strColumns(0))
										dblMZToleranceDa = Double.Parse(strColumns(1))
										If dblMZToleranceDa = 0 Then
											dblMZToleranceDa = dblDefaultMZTolerance
										End If

										sngScanOrAcqTime = Single.Parse(strColumns(2))

										If strColumns.Length >= 4 Then
											sngScanOrAcqTimeTolerance = Single.Parse(strColumns(3))
										Else
											sngScanOrAcqTimeTolerance = sngDefaultScanOrAcqTimeTolerance
										End If

										If strColumns.Length >= 5 Then
											strComment = strColumns(4)
										Else
											strComment = String.Empty
										End If

									End If

									If dblMZ > 0 Then
										blnExistingRowFound = False
										AddCustomSICRow(dblMZ, dblMZToleranceDa, sngScanOrAcqTime, sngScanOrAcqTimeTolerance, strComment, blnExistingRowFound)

										If blnExistingRowFound Then
											intRowsAlreadyPresent += 1
										End If
									End If

								Catch ex As Exception
									' Skip this row
									intRowsSkipped += 1
								End Try
							Else
								intRowsSkipped += 1
							End If
						End If
					Next intLineIndex

					If intRowsAlreadyPresent > 0 Then
						If intRowsAlreadyPresent = 1 Then
							strMessage = "1 row of thresholds was"
						Else
							strMessage = intRowsAlreadyPresent.ToString & " rows of thresholds were"
						End If

						Windows.Forms.MessageBox.Show(strMessage & " already present in the table; duplicate rows are not allowed.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
					End If

					If intRowsSkipped > 0 Then
						If intRowsSkipped = 1 Then
							strMessage = "1 row was skipped because it"
						Else
							strMessage = intRowsSkipped.ToString & " rows were skipped because they"
						End If

						Windows.Forms.MessageBox.Show(strMessage & " didn't contain two columns of numeric data.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
					End If

				End If
			End If

		End If
	End Sub

	Private Sub PopulateComboBoxes()

		With cboExportRawDataFileFormat
			With .Items
				.Clear()
				.Insert(clsMASIC.eExportRawDataFileFormatConstants.PEKFile, "PEK File")
				.Insert(clsMASIC.eExportRawDataFileFormatConstants.CSVFile, "DeconTools CSV File")
			End With
			.SelectedIndex = clsMASIC.eExportRawDataFileFormatConstants.CSVFile
		End With

		With cboSICNoiseThresholdMode
			With .Items
				.Clear()
				.Insert(eNoiseThresholdModes.AbsoluteThreshold, "Absolute Threshold")
				.Insert(eNoiseThresholdModes.TrimmedMeanByAbundance, "Trimmed Mean By Abundance")
				.Insert(eNoiseThresholdModes.TrimmedMeanByCount, "Trimmed Mean By Data Count")
				.Insert(eNoiseThresholdModes.TrimmedMedianByAbundance, "Trimmed Median By Abundance")
				.Insert(eNoiseThresholdModes.DualTrimmedMeanByAbundance, "Dual Trimmed Mean By Abundance")
			End With
			.SelectedIndex = eNoiseThresholdModes.DualTrimmedMeanByAbundance
		End With

		With cboMassSpectraNoiseThresholdMode
			With .Items
				.Clear()
				.Insert(eNoiseThresholdModes.AbsoluteThreshold, "Absolute Threshold")
				.Insert(eNoiseThresholdModes.TrimmedMeanByAbundance, "Trimmed Mean By Abundance")
				.Insert(eNoiseThresholdModes.TrimmedMeanByCount, "Trimmed Mean By Data Count")
				.Insert(eNoiseThresholdModes.TrimmedMedianByAbundance, "Trimmed Median By Abundance")
			End With
			.SelectedIndex = eNoiseThresholdModes.TrimmedMedianByAbundance
		End With

		With cboReporterIonMassMode
			With .Items
				.Clear()
				.Insert(eReporterIonMassModeConstants.CustomOrNone, "None")
				.Insert(eReporterIonMassModeConstants.ITraqFourMZ, "iTraq: 114, 115, 116, and 117")
				.Insert(eReporterIonMassModeConstants.ITraqETDThreeMZ, "iTraq ETD: 101, 102, and 104")
				.Insert(eReporterIonMassModeConstants.TMTTwoMZ, "TMT 2: 126, 127")
				.Insert(eReporterIonMassModeConstants.TMTSixMZ, "TMT 6: 126, 127, 128, 129, 130, 131")
				.Insert(eReporterIonMassModeConstants.ITraqEightMZHighRes, "iTraq 8 for High Res MS/MS: 113, 114, ... 121")
				.Insert(eReporterIonMassModeConstants.ITraqEightMZLowRes, "iTraq 8 for Low Res MS/MS (Considers 120 m/z for immonium loss from phenylalanine)")
				.Insert(eReporterIonMassModeConstants.PCGalnaz, "PCGalnaz: 300.13 and 503.21")
				.Insert(eReporterIonMassModeConstants.HemeCFragment, "Heme C: 616.18 and 617.19")
				.Insert(eReporterIonMassModeConstants.LycAcetFragment, "Lys Acet: 126.091 and 127.095")
				.Insert(eReporterIonMassModeConstants.TMTTenMZ, "TMT 10: 126, 127N, 127C, 128N, 128C, 129N, 129C, 130N, 130C, 131")
				.Insert(eReporterIonMassModeConstants.OGlcNAc, "OGlcNAc: 204.087, 300.13, and 503.21")
			End With
			.SelectedIndex = clsMASIC.eReporterIonMassModeConstants.CustomOrNone
		End With

	End Sub

	Private Sub ProcessFileUsingMASIC()

		Dim strOutputFolderPath As String
		Dim blnSuccess As Boolean

		Dim dtStartTime As DateTime

		If Not mWorking AndAlso ConfirmPaths() Then
			Try

				If mMasic Is Nothing Then
					mMasic = New clsMASIC
				End If
				mMasic.ShowMessages = True

				' Configure settings
				blnSuccess = UpdateMasicSettings(mMasic)
				If Not blnSuccess Then Exit Try

				' Validate settings
				blnSuccess = ValidateSettings(mMasic)
				If Not blnSuccess Then Exit Try

				mProgressForm = New ProgressFormNET.frmProgress

				mProgressForm.InitializeProgressForm("Creating SIC's for the parent ions", 0, 100, False, True)
				mProgressForm.InitializeSubtask("", 0, 100, False)
				mProgressForm.ResetKeyPressAbortProcess()
				mProgressForm.Show()
				Application.DoEvents()

				With mMasic
					Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor
					mWorking = True
					cmdStartProcessing.Enabled = False
					Application.DoEvents()

					dtStartTime = System.DateTime.UtcNow

					strOutputFolderPath = txtOutputFolderPath.Text
					blnSuccess = .ProcessFile(txtInputFilePath.Text, strOutputFolderPath)

					Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default

					If blnSuccess Then
						' Grab the status message, but insert a carriage return directly after "in folder:"
						Windows.Forms.MessageBox.Show(.StatusMessage.Replace("in folder:", "in folder:" & ControlChars.NewLine) & ControlChars.NewLine & "Elapsed time: " & Math.Round(System.DateTime.UtcNow.Subtract(dtStartTime).TotalSeconds, 2).ToString & " sec", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information)
					Else
						Windows.Forms.MessageBox.Show("Error analyzing input file with MASIC: " & ControlChars.NewLine & .GetErrorMessage() & ControlChars.NewLine & .StatusMessage, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
					End If
				End With
				mMasic = Nothing

			Catch ex As Exception
				Windows.Forms.MessageBox.Show("Error in frmMain->ProcessFileUsingMASIC: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
			Finally
				If Not mProgressForm Is Nothing Then
					mProgressForm.HideForm()
					mProgressForm = Nothing
				End If

				mWorking = False
				cmdStartProcessing.Enabled = True
				mMasic = Nothing
			End Try
		End If
	End Sub

	Private Sub ResetToDefaults(ByVal blnConfirm As Boolean, Optional ByRef objMasic As clsMASIC = Nothing)

		Dim eResponse As System.Windows.Forms.DialogResult
		Dim blnExistingMasicObjectUsed As Boolean

		Dim dblSICTolerance As Double, blnSICToleranceIsPPM As Boolean

		Dim udtCustomMZList() As clsMASIC.udtCustomMZSearchSpecType
		Dim intIndex As Integer

		If blnConfirm Then
			eResponse = System.Windows.Forms.MessageBox.Show("Are you sure you want to reset all settings to their default values?", "Reset to Defaults", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
			If eResponse <> DialogResult.Yes Then Exit Sub
		End If

		If objMasic Is Nothing Then
			objMasic = New clsMASIC
			blnExistingMasicObjectUsed = False
		Else
			blnExistingMasicObjectUsed = True
		End If

		Me.Width = 710
		Me.Height = 560

		' File Paths and Import Options
		Try
			If txtOutputFolderPath.TextLength = 0 OrElse Not System.IO.Directory.Exists(txtOutputFolderPath.Text) Then
				txtOutputFolderPath.Text = clsProcessFilesBaseClass.GetAppFolderPath()
			End If
		Catch ex As Exception
			If blnConfirm Then
				System.Windows.Forms.MessageBox.Show("Exception occurred while validating txtOutputFolderPath.Text: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
			End If
		End Try

		Try
			With objMasic
				' Import Options
				mUseFinniganXRawAccessorFunctions = .UseFinniganXRawAccessorFunctions
				txtParentIonDecoyMassDa.Text = .ParentIonDecoyMassDa.ToString

				' Masic Export Options
				chkIncludeHeaders.Checked = .IncludeHeadersInExportFile
				chkIncludeScanTimesInSICStatsFile.Checked = .IncludeScanTimesInSICStatsFile

				chkSkipMSMSProcessing.Checked = .SkipMSMSProcessing
				chkSkipSICAndRawDataProcessing.Checked = .SkipSICAndRawDataProcessing
				chkExportRawDataOnly.Checked = .ExportRawDataOnly

				' Raw data export options
				chkExportRawSpectraData.Checked = .ExportRawSpectraData					' Create .PEK file, or similar
				cboExportRawDataFileFormat.SelectedIndex = .ExportRawDataFileFormat		' ToDo: Add XML

				chkExportRawDataIncludeMSMS.Checked = .ExportRawDataIncludeMSMS
				chkExportRawDataRenumberScans.Checked = .ExportRawDataRenumberScans

				txtExportRawDataSignalToNoiseRatioMinimum.Text = .ExportRawDataMinimumSignalToNoiseRatio.ToString
				txtExportRawDataMaxIonCountPerScan.Text = .ExportRawDataMaxIonCountPerScan.ToString
				txtExportRawDataIntensityMinimum.Text = .ExportRawDataIntensityMinimum.ToString

				' Finnigan Info File options
				chkSaveMSMethodFile.Checked = .WriteMSMethodFile
				chkSaveMSTuneFile.Checked = .WriteMSTuneFile
				chkWriteDetailedSICDataFile.Checked = .WriteDetailedSICDataFile
				chkSaveExtendedStatsFile.Checked = .WriteExtendedStats
				chkSaveExtendedStatsFileIncludeFilterText.Checked = .WriteExtendedStatsIncludeScanFilterText
				chkSaveExtendedStatsFileIncludeStatusLog.Checked = .WriteExtendedStatsStatusLog
				txtStatusLogKeyNameFilterList.Text = .GetStatusLogKeyNameFilterListAsText(False)

				chkConsolidateConstantExtendedHeaderValues.Checked = .ConsolidateConstantExtendedHeaderValues

				' Dataset and Database Options
				txtDatasetNumber.Text = "0"
				txtDatabaseConnectionString.Text = .DatabaseConnectionString
				txtDatasetInfoQuerySQL.Text = .DatasetInfoQuerySql

				Try
					If System.IO.File.Exists(.DatasetLookupFilePath) Then
						txtDatasetLookupFilePath.Text = .DatasetLookupFilePath
					Else
						txtDatasetLookupFilePath.Text = String.Empty
					End If
				Catch ex As Exception
					txtDatasetLookupFilePath.Text = String.Empty
				End Try

				' SIC Options
				dblSICTolerance = .GetSICTolerance(blnSICToleranceIsPPM)
				txtSICTolerance.Text = Math.Round(dblSICTolerance, 6).ToString
				If blnSICToleranceIsPPM Then
					optSICTolerancePPM.Checked = True
				Else
					optSICToleranceDa.Checked = True
				End If

				txtScanStart.Text = .ScanRangeStart.ToString
				txtScanEnd.Text = .ScanRangeEnd.ToString
				txtTimeStart.Text = .RTRangeStart.ToString
				txtTimeEnd.Text = .RTRangeEnd.ToString

				' Note: the following 5 options are not graphically editable
				mSuppressNoParentIonsError = .SuppressNoParentIonsError
				mCompressMSSpectraData = .CompressMSSpectraData
				mCompressMSMSSpectraData = .CompressMSMSSpectraData
				mCompressToleranceDivisorForDa = .CompressToleranceDivisorForDa
				mCompressToleranceDivisorForPPM = .CompressToleranceDivisorForPPM

				txtMaxPeakWidthMinutesBackward.Text = .MaxSICPeakWidthMinutesBackward.ToString
				txtMaxPeakWidthMinutesForward.Text = .MaxSICPeakWidthMinutesForward.ToString

				txtIntensityThresholdFractionMax.Text = .IntensityThresholdFractionMax.ToString
				txtIntensityThresholdAbsoluteMinimum.Text = .IntensityThresholdAbsoluteMinimum.ToString

				chkReplaceSICZeroesWithMinimumPositiveValueFromMSData.Checked = .ReplaceSICZeroesWithMinimumPositiveValueFromMSData
				chkRefineReportedParentIonMZ.Checked = .RefineReportedParentIonMZ

				' Peak Finding Options
				cboSICNoiseThresholdMode.SelectedIndex = .SICNoiseThresholdMode
				txtSICNoiseThresholdIntensity.Text = .SICNoiseThresholdIntensity.ToString
				txtSICNoiseFractionLowIntensityDataToAverage.Text = .SICNoiseFractionLowIntensityDataToAverage.ToString
				txtSICNoiseMinimumSignalToNoiseRatio.Text = .SICNoiseMinimumSignalToNoiseRatio.ToString

				txtMaxDistanceScansNoOverlap.Text = .MaxDistanceScansNoOverlap.ToString
				txtMaxAllowedUpwardSpikeFractionMax.Text = .MaxAllowedUpwardSpikeFractionMax.ToString
				txtInitialPeakWidthScansScaler.Text = .InitialPeakWidthScansScaler.ToString
				txtInitialPeakWidthScansMaximum.Text = .InitialPeakWidthScansMaximum.ToString

				If .UseButterworthSmooth Then
					optUseButterworthSmooth.Checked = True
					optUseSavitzkyGolaySmooth.Checked = False
				Else
					optUseButterworthSmooth.Checked = False
					optUseSavitzkyGolaySmooth.Checked = True
				End If

				txtButterworthSamplingFrequency.Text = .ButterworthSamplingFrequency.ToString
				txtSavitzkyGolayFilterOrder.Text = .SavitzkyGolayFilterOrder.ToString

				chkFindPeaksOnSmoothedData.Checked = .FindPeaksOnSmoothedData
				chkSmoothDataRegardlessOfMinimumPeakWidth.Checked = .SmoothDataRegardlessOfMinimumPeakWidth

				' Mass Spectra Noise Threshold Options
				cboMassSpectraNoiseThresholdMode.SelectedIndex = .MassSpectraNoiseThresholdMode
				txtMassSpectraNoiseThresholdIntensity.Text = .MassSpectraNoiseThresholdIntensity.ToString
				txtMassSpectraNoiseFractionLowIntensityDataToAverage.Text = .MassSpectraNoiseFractionLowIntensityDataToAverage.ToString
				txtMassSpectraNoiseMinimumSignalToNoiseRatio.Text = .MassSpectraNoiseMinimumSignalToNoiseRatio.ToString

				' Similarity Options
				txtSimilarIonMZToleranceHalfWidth.Text = .SimilarIonMZToleranceHalfWidth.ToString
				txtSimilarIonToleranceHalfWidthMinutes.Text = .SimilarIonToleranceHalfWidthMinutes.ToString
				txtSpectrumSimilarityMinimum.Text = .SpectrumSimilarityMinimum.ToString

				' Binning Options
				txtBinStartX.Text = .BinStartX.ToString
				txtBinEndX.Text = .BinEndX.ToString
				txtBinSize.Text = .BinSize.ToString
				txtMaximumBinCount.Text = .MaximumBinCount.ToString

				txtBinnedDataIntensityPrecisionPct.Text = .BinnedDataIntensityPrecisionPercent.ToString

				chkBinnedDataNormalize.Checked = .NormalizeBinnedData
				chkBinnedDataSumAllIntensitiesForBin.Checked = .SumAllIntensitiesForBin

				' Spectrum caching options (not graphically editable)
				mCacheOptions.DiskCachingAlwaysDisabled = .DiskCachingAlwaysDisabled
				mCacheOptions.FolderPath = .CacheFolderPath
				mCacheOptions.SpectraToRetainInMemory = .CacheSpectraToRetainInMemory
				mCacheOptions.MinimumFreeMemoryMB = .CacheMinimumFreeMemoryMB
				mCacheOptions.MaximumMemoryUsageMB = .CacheMaximumMemoryUsageMB

				' Reporter ion options
				txtReporterIonMZToleranceDa.Text = Math.Round(.ReporterIonToleranceDaDefault, 6).ToString

				cboReporterIonMassMode.SelectedIndex = .ReporterIonMassMode

				chkReporterIonStatsEnabled.Checked = .ReporterIonStatsEnabled
				chkReporterIonApplyAbundanceCorrection.Checked = .ReporterIonApplyAbundanceCorrection

				chkReporterIonSaveObservedMasses.Checked = .ReporterIonSaveObservedMasses
				chkReporterIonSaveUncorrectedIntensities.Checked = .ReporterIonSaveUncorrectedIntensities

				' MRM Options
				chkMRMWriteDataList.Checked = .WriteMRMDataList
				chkMRMWriteIntensityCrosstab.Checked = .WriteMRMIntensityCrosstab

				' Custom SIC Options
				txtCustomSICFileName.Text = .CustomSICListFileName

				chkLimitSearchToCustomMZs.Checked = .LimitSearchToCustomMZList
				SetCustomSICToleranceType(.CustomSICListScanType)

				txtCustomSICScanOrAcqTimeTolerance.Text = .CustomSICListScanTolerance.ToString

				' Load the Custom m/z values from mCustomSICList
				udtCustomMZList = .CustomSICListSearchValues()

			End With

			ClearCustomSICList(False)
			For intIndex = 0 To udtCustomMZList.Length - 1
				With udtCustomMZList(intIndex)
					AddCustomSICRow(.MZ, .ScanOrAcqTimeCenter, .Comment)
				End With
			Next intIndex

		Catch ex As Exception
			If blnConfirm Then
				System.Windows.Forms.MessageBox.Show("Error resetting values to defaults: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
			End If
		End Try

		If Not blnExistingMasicObjectUsed Then
			objMasic = Nothing
		End If

	End Sub

	Private Sub SelectDatasetLookupFile()
		Dim objOpenFile As New System.Windows.Forms.OpenFileDialog

		With objOpenFile
			.AddExtension = True
			.CheckFileExists = True
			.CheckPathExists = True
			.DefaultExt = ".txt"
			.DereferenceLinks = True
			.Multiselect = False
			.ValidateNames = True

			.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
			.FilterIndex = 1

			If txtDatasetLookupFilePath.TextLength > 0 Then
				Try
					.InitialDirectory = System.IO.Directory.GetParent(txtDatasetLookupFilePath.Text).ToString
				Catch
					.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
				End Try
			Else
				.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
			End If

			.Title = "Select dataset lookup file"

			.ShowDialog()
			If .FileName.Length > 0 Then
				txtDatasetLookupFilePath.Text = .FileName
			End If
		End With

	End Sub

	Private Sub SelectCustomSICFile()

		Dim objOpenFile As New System.Windows.Forms.OpenFileDialog
		Dim strExtension As String

		With objOpenFile
			.AddExtension = True
			.CheckFileExists = True
			.CheckPathExists = True
			.DefaultExt = ".txt"
			.DereferenceLinks = True
			.Multiselect = False
			.ValidateNames = True

			.Filter = "Text files (*.txt)|*.txt|" & _
			 "CSV files (*.csv)|*.csv|" & _
			 "All files (*.*)|*.*"

			strExtension = ".txt"

			If txtCustomSICFileName.TextLength > 0 Then
				strExtension = System.IO.Path.GetExtension(txtCustomSICFileName.Text)
			End If

			Select Case strExtension.ToLower
				Case ".txt"
					.FilterIndex = 1
				Case "csv"
					.FilterIndex = 2
				Case Else
					.FilterIndex = 1
			End Select

			If txtCustomSICFileName.TextLength > 0 Then
				Try
					.InitialDirectory = System.IO.Directory.GetParent(txtCustomSICFileName.Text).ToString
				Catch
					.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
				End Try
			Else
				.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
			End If

			.Title = "Select custom SIC values file"

			.ShowDialog()
			If .FileName.Length > 0 Then
				txtCustomSICFileName.Text = .FileName
			End If
		End With

	End Sub

	Private Sub SelectInputFile()

		Dim objOpenFile As New System.Windows.Forms.OpenFileDialog
		Dim strExtension As String

		With objOpenFile
			.AddExtension = True
			.CheckFileExists = True
			.CheckPathExists = True
			.DefaultExt = ".txt"
			.DereferenceLinks = True
			.Multiselect = False
			.ValidateNames = True

			.Filter = "Xcalibur Raw files (*.raw)|*.raw|" & _
			 "mzXML files (*.mzXML)|*.mzXML|" & _
			 "mzXML files (*mzXML.xml)|*mzXML.xml|" & _
			 "mzData files (*.mzData)|*.mzData|" & _
			 "mzData files (*mzData.xml)|*mzData.xml|" & _
			 "Mascot Generic Format files (*.mgf)|*.mgf|" & _
			 "CDF files (*.cdf)|*.cdf|" & _
			 "All files (*.*)|*.*"

			strExtension = String.Copy(mPreferredInputFileExtension)

			If txtInputFilePath.TextLength > 0 Then
				strExtension = System.IO.Path.GetExtension(txtInputFilePath.Text)
			End If

			Select Case strExtension.ToLower
				Case ".mzxml"
					.FilterIndex = 2
				Case "mzxml.xml"
					.FilterIndex = 3
				Case ".mzdata"
					.FilterIndex = 4
				Case "mzdata.xml"
					.FilterIndex = 5
				Case ".mgf"
					.FilterIndex = 6
				Case ".cdf"
					.FilterIndex = 7
				Case Else
					.FilterIndex = 1
			End Select

			If txtInputFilePath.TextLength > 0 Then
				Try
					.InitialDirectory = System.IO.Directory.GetParent(txtInputFilePath.Text).ToString
				Catch
					.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
				End Try
			Else
				.InitialDirectory = clsProcessFilesBaseClass.GetAppFolderPath()
			End If

			.Title = "Select input file"

			.ShowDialog()
			If .FileName.Length > 0 Then
				txtInputFilePath.Text = .FileName
				mPreferredInputFileExtension = System.IO.Path.GetExtension(.FileName)
			End If
		End With

	End Sub

	Private Sub SelectOutputFolder()

		Dim objFolderBrowserDialog As New PRISM.Files.FolderBrowser

		With objFolderBrowserDialog
			' No need to set the Browse Flags; default values are already set

			If txtOutputFolderPath.TextLength > 0 Then
				.FolderPath = txtOutputFolderPath.Text
			End If

			If .BrowseForFolder() Then
				txtOutputFolderPath.Text = .FolderPath
			End If
		End With
	End Sub

	Private Sub SetConnectionStringToPNNLServer()
		txtDatabaseConnectionString.Text = clsMASIC.DATABASE_CONNECTION_STRING_DEFAULT
		txtDatasetInfoQuerySQL.Text = clsMASIC.DATABASE_DATASET_INFO_QUERY_DEFAULT
	End Sub

	Private Sub SetCustomSICToleranceType(ByVal eCustomSICScanToleranceType As clsMASIC.eCustomSICScanTypeConstants)
		Select Case eCustomSICScanToleranceType
			Case clsMASIC.eCustomSICScanTypeConstants.Absolute
				optCustomSICScanToleranceAbsolute.Checked = True

			Case clsMASIC.eCustomSICScanTypeConstants.Relative
				optCustomSICScanToleranceRelative.Checked = True

			Case clsMASIC.eCustomSICScanTypeConstants.AcquisitionTime
				optCustomSICScanToleranceAcqTime.Checked = True

			Case Else
				optCustomSICScanToleranceAbsolute.Checked = True
		End Select
	End Sub

	Public Sub SetHeightAdjustForce(ByVal intHeightToForce As Integer)
		' This function can be used to prevent the form from resizing itself if the MyBase.Resize event
		'  fires within 2 seconds of the current time
		' See CatchUnrequestedHeightChange for more info        
		mHeightAdjustForce = intHeightToForce
		mHeightAdjustTime = System.DateTime.UtcNow
	End Sub

	Private Sub SetToolTips()
		Dim objToolTipControl As New System.Windows.Forms.ToolTip

		With objToolTipControl
			.SetToolTip(txtDatasetNumber, "The dataset number is included as the first column in the output file.")

			.SetToolTip(txtIntensityThresholdAbsoluteMinimum, "Threshold for extending SIC")
			.SetToolTip(txtMaxDistanceScansNoOverlap, "Maximum distance that the edge of an identified peak can be away from the scan number that the parent ion was observed in if the identified peak does not contain the parent ion.")
			.SetToolTip(txtMaxAllowedUpwardSpikeFractionMax, "Maximum fraction of the peak maximum that an upward spike can be to be included in the peak")
			.SetToolTip(txtInitialPeakWidthScansScaler, "Multiplied by the S/N for the given spectrum to determine the initial minimum peak width (in scans) to try")
			.SetToolTip(txtInitialPeakWidthScansMaximum, "Maximum initial peak width to allow")

			.SetToolTip(txtSICTolerance, "Search tolerance for creating SIC; suggest 0.6 Da for ion traps and 20 ppm for TOF, FT or Orbitrap instruments")
			.SetToolTip(txtButterworthSamplingFrequency, "Value between 0.01 and 0.99; suggested value is 0.25")
			.SetToolTip(txtSavitzkyGolayFilterOrder, "Even number, 0 or greater; 0 means a moving average filter, 2 means a 2nd order Savitzky Golay filter")

			.SetToolTip(chkRefineReportedParentIonMZ, "If enabled, then will look through the m/z values in the parent ion spectrum data to find the closest match (within SICTolerance / " & clsMASIC.DEFAULT_COMPRESS_TOLERANCE_DIVISOR_FOR_DA.ToString & "); will update the reported m/z value to the one found")

			.SetToolTip(txtStatusLogKeyNameFilterList, "Enter a comma and/or NewLine separated list of Status Log Key names to match (will match any part of the key name to the text you enter).  Leave blank to include all Status Log entries.")
		End With

		objToolTipControl = Nothing

	End Sub

	Private Sub ShowAboutBox()
		Dim strMessage As String

		If mMasic Is Nothing Then
			mMasic = New clsMASIC
		End If

		strMessage = String.Empty

		strMessage &= "Program written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2003" & ControlChars.NewLine
		strMessage &= "Copyright 2005, Battelle Memorial Institute.  All Rights Reserved." & ControlChars.NewLine & ControlChars.NewLine

		strMessage &= "This is version " & System.Windows.Forms.Application.ProductVersion & " (" & PROGRAM_DATE & "). "
		strMessage &= "Using MASIC PeakFinder DLL version " & mMasic.MASICPeakFinderDllVersion & ControlChars.NewLine & ControlChars.NewLine

		strMessage &= "E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com" & ControlChars.NewLine
		strMessage &= "Website: http://panomics.pnnl.gov/ or http://www.sysbio.org/resources/staff/" & ControlChars.NewLine & ControlChars.NewLine

		strMessage &= "Licensed under the Apache License, Version 2.0; you may not use this file except in compliance with the License.  "
		strMessage &= "You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0" & ControlChars.NewLine & ControlChars.NewLine

		strMessage &= "Notice: This computer software was prepared by Battelle Memorial Institute, "
		strMessage &= "hereinafter the Contractor, under Contract No. DE-AC05-76RL0 1830 with the "
		strMessage &= "Department of Energy (DOE).  All rights in the computer software are reserved "
		strMessage &= "by DOE on behalf of the United States Government and the Contractor as "
		strMessage &= "provided in the Contract.  NEITHER THE GOVERNMENT NOR THE CONTRACTOR MAKES ANY "
		strMessage &= "WARRANTY, EXPRESS OR IMPLIED, OR ASSUMES ANY LIABILITY FOR THE USE OF THIS "
		strMessage &= "SOFTWARE.  This notice including this sentence must appear on any copies of "
		strMessage &= "this computer software." & ControlChars.NewLine

		Windows.Forms.MessageBox.Show(strMessage, "About", MessageBoxButtons.OK, MessageBoxIcon.Information)

	End Sub

	Private Sub UpdateCustomSICDataGridTableStyle()
		Dim tsCustomSICValues As System.Windows.Forms.DataGridTableStyle
		Dim blnTimeTolerance As Boolean

		' Define the PM Thresholds table style 
		tsCustomSICValues = New System.Windows.Forms.DataGridTableStyle

		' Setting the MappingName of the table style to CUSTOM_SIC_VALUES_DATATABLE will cause this style to be used with that table
		With tsCustomSICValues
			.MappingName = CUSTOM_SIC_VALUES_DATATABLE
			.AllowSorting = True
			.ColumnHeadersVisible = True
			.RowHeadersVisible = True
			.ReadOnly = False
		End With

		SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_MZ, "Custom m/z", 90)
		SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_MZ_TOLERANCE, "m/z tolerance (Da)", 110)

		blnTimeTolerance = False
		Select Case GetCustomSICScanToleranceType()
			Case clsMASIC.eCustomSICScanTypeConstants.Relative
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_CENTER, "Relative Scan Number (0 to 1)", 170)
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_TOLERANCE, "Scan Tolerance", 90)

			Case clsMASIC.eCustomSICScanTypeConstants.AcquisitionTime
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_CENTER, "Acq time (minutes)", 110)
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_TOLERANCE, "Time Tolerance", 90)
				blnTimeTolerance = True

			Case Else
				' Includes clsMASIC.eCustomSICScanTypeConstants.Absolute
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_CENTER, "Scan Number", 90)
				SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_TOLERANCE, "Scan Tolerance", 90)
		End Select

		SharedVBNetRoutines.ADONetRoutines.AppendColumnToTableStyle(tsCustomSICValues, COL_NAME_SCAN_COMMENT, "Comment", 90)

		fraCustomSICControls.Left = dgCustomSICValues.Left + dgCustomSICValues.Width + 15

		With dgCustomSICValues
			.TableStyles.Clear()

			If Not .TableStyles.Contains(tsCustomSICValues) Then
				.TableStyles.Add(tsCustomSICValues)
			End If

			.Refresh()
		End With

		If blnTimeTolerance Then
			lblCustomSICScanTolerance.Text = "Time Tolerance"
		Else
			lblCustomSICScanTolerance.Text = "Scan Tolerance"
		End If

	End Sub

	Private Function UpdateMasicSettings(ByRef objMasic As clsMASIC) As Boolean

		Dim objSharedRoutines As New SharedVBNetRoutines.VBNetRoutines
		Dim blnError As Boolean

		Dim eScanType As clsMASIC.eCustomSICScanTypeConstants
		Dim sngScanOrAcqTimeTolerance As Single

		Dim dblSICTolerance As Double

		Dim intCustomSICListCount As Integer
		Dim dblMZList() As Double
		Dim dblMZToleranceList() As Double
		Dim sngScanOrAcqTimeList() As Single
		Dim sngScanOrAcqTimeToleranceList() As Single
		Dim strScanComments() As String

		Dim strCustomSICFileName As String

		Dim myDataRow As DataRow

		Try
			With objMasic
				' Import options
				.UseFinniganXRawAccessorFunctions = mUseFinniganXRawAccessorFunctions

				.ParentIonDecoyMassDa = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueDbl(txtParentIonDecoyMassDa, lblParentIonDecoyMassDa.Text & " must be a value", blnError)
				If blnError Then Exit Try

				' Masic Export Options
				.IncludeHeadersInExportFile = chkIncludeHeaders.Checked
				.IncludeScanTimesInSICStatsFile = chkIncludeScanTimesInSICStatsFile.Checked

				.SkipMSMSProcessing = chkSkipMSMSProcessing.Checked
				.SkipSICAndRawDataProcessing = chkSkipSICAndRawDataProcessing.Checked
				.ExportRawDataOnly = chkExportRawDataOnly.Checked

				' Raw data export options
				.ExportRawSpectraData = chkExportRawSpectraData.Checked
				.ExportRawDataFileFormat = CType(cboExportRawDataFileFormat.SelectedIndex, clsMASIC.eExportRawDataFileFormatConstants)

				.ExportRawDataIncludeMSMS = chkExportRawDataIncludeMSMS.Checked
				.ExportRawDataRenumberScans = chkExportRawDataRenumberScans.Checked

				.ExportRawDataMinimumSignalToNoiseRatio = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtExportRawDataSignalToNoiseRatioMinimum, lblExportRawDataSignalToNoiseRatioMinimum.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.ExportRawDataMaxIonCountPerScan = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtExportRawDataMaxIonCountPerScan, lblExportRawDataMaxIonCountPerScan.Text & " must be an integer value", blnError)
				If blnError Then Exit Try
				.ExportRawDataIntensityMinimum = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtExportRawDataIntensityMinimum, lblExportRawDataIntensityMinimum.Text & " must be a value", blnError)
				If blnError Then Exit Try

				' Finnigan Info File options
				.WriteMSMethodFile = chkSaveMSMethodFile.Checked
				.WriteMSTuneFile = chkSaveMSTuneFile.Checked
				.WriteDetailedSICDataFile = chkWriteDetailedSICDataFile.Checked
				.WriteExtendedStats = chkSaveExtendedStatsFile.Checked
				.WriteExtendedStatsIncludeScanFilterText = chkSaveExtendedStatsFileIncludeFilterText.Checked
				.WriteExtendedStatsStatusLog = chkSaveExtendedStatsFileIncludeStatusLog.Checked
				.SetStatusLogKeyNameFilterList(txtStatusLogKeyNameFilterList.Text, ","c)

				.ConsolidateConstantExtendedHeaderValues = chkConsolidateConstantExtendedHeaderValues.Checked

				' Dataset and Database options
				.DatasetNumber = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtDatasetNumber, lblDatasetNumber.Text & " must be an integer value", blnError)
				If blnError Then Exit Try

				If txtDatabaseConnectionString.TextLength > 0 And txtDatasetInfoQuerySQL.TextLength > 0 Then
					.DatabaseConnectionString = txtDatabaseConnectionString.Text
					.DatasetInfoQuerySql = txtDatasetInfoQuerySQL.Text
				Else
					.DatabaseConnectionString = String.Empty
					.DatasetInfoQuerySql = String.Empty
				End If

				Try
					If System.IO.File.Exists(txtDatasetLookupFilePath.Text) Then
						.DatasetLookupFilePath = txtDatasetLookupFilePath.Text
					Else
						.DatasetLookupFilePath = String.Empty
					End If
				Catch ex As Exception
					.DatasetLookupFilePath = String.Empty
				End Try

				' SIC Options
				dblSICTolerance = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueDbl(txtSICTolerance, lblSICToleranceDa.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.SetSICTolerance(dblSICTolerance, optSICTolerancePPM.Checked)

				.ScanRangeStart = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtScanStart, lblScanStart.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.ScanRangeEnd = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtScanEnd, lblScanEnd.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.RTRangeStart = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtTimeStart, lblTimeStart.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.RTRangeEnd = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtTimeEnd, lblTimeEnd.Text & " must be a value", blnError)
				If blnError Then Exit Try

				' Note: the following 5 options are not graphically editable
				.SuppressNoParentIonsError = mSuppressNoParentIonsError
				.CompressMSSpectraData = mCompressMSSpectraData
				.CompressMSMSSpectraData = mCompressMSMSSpectraData
				.CompressToleranceDivisorForDa = mCompressToleranceDivisorForDa
				.CompressToleranceDivisorForPPM = mCompressToleranceDivisorForPPM

				.MaxSICPeakWidthMinutesBackward = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMaxPeakWidthMinutesBackward, lblMaxPeakWidthMinutes.Text & " " & lblMaxPeakWidthMinutesBackward.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.MaxSICPeakWidthMinutesForward = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMaxPeakWidthMinutesForward, lblMaxPeakWidthMinutes.Text & " " & lblMaxPeakWidthMinutesForward.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.IntensityThresholdFractionMax = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtIntensityThresholdFractionMax, lblIntensityThresholdFractionMax.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.IntensityThresholdAbsoluteMinimum = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtIntensityThresholdAbsoluteMinimum, lblIntensityThresholdAbsoluteMinimum.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.ReplaceSICZeroesWithMinimumPositiveValueFromMSData = chkReplaceSICZeroesWithMinimumPositiveValueFromMSData.Checked
				.RefineReportedParentIonMZ = chkRefineReportedParentIonMZ.Checked

				' Peak Finding Options
				.SICNoiseThresholdMode = CType(cboSICNoiseThresholdMode.SelectedIndex, eNoiseThresholdModes)
				.SICNoiseThresholdIntensity = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtSICNoiseThresholdIntensity, lblSICNoiseThresholdIntensity.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.SICNoiseFractionLowIntensityDataToAverage = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtSICNoiseFractionLowIntensityDataToAverage, lblSICNoiseFractionLowIntensityDataToAverage.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.SICNoiseMinimumSignalToNoiseRatio = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtSICNoiseMinimumSignalToNoiseRatio, lblSICNoiseMinimumSignalToNoiseRatio.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.MaxDistanceScansNoOverlap = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtMaxDistanceScansNoOverlap, lblMaxDistanceScansNoOverlap.Text & " must be an integer value", blnError)
				If blnError Then Exit Try
				.MaxAllowedUpwardSpikeFractionMax = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMaxAllowedUpwardSpikeFractionMax, lblMaxAllowedUpwardSpikeFractionMax.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.InitialPeakWidthScansScaler = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtInitialPeakWidthScansScaler, lblInitialPeakWidthScansScaler.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.InitialPeakWidthScansMaximum = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtInitialPeakWidthScansMaximum, lblInitialPeakWidthScansMaximum.Text & " must be an integer value", blnError)
				If blnError Then Exit Try

				.UseButterworthSmooth = optUseButterworthSmooth.Checked
				.ButterworthSamplingFrequency = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtButterworthSamplingFrequency, lblButterworthSamplingFrequency.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.UseSavitzkyGolaySmooth = optUseSavitzkyGolaySmooth.Checked
				.SavitzkyGolayFilterOrder = CShort(SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtSavitzkyGolayFilterOrder, lblSavitzkyGolayFilterOrder.Text & " must be an integer value", blnError))
				If blnError Then Exit Try

				.FindPeaksOnSmoothedData = chkFindPeaksOnSmoothedData.Checked
				.SmoothDataRegardlessOfMinimumPeakWidth = chkSmoothDataRegardlessOfMinimumPeakWidth.Checked

				' Mass Spectra Noise Threshold Options
				.MassSpectraNoiseThresholdMode = CType(cboMassSpectraNoiseThresholdMode.SelectedIndex, eNoiseThresholdModes)
				.MassSpectraNoiseThresholdIntensity = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMassSpectraNoiseThresholdIntensity, lblMassSpectraNoiseThresholdIntensity.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.MassSpectraNoiseFractionLowIntensityDataToAverage = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMassSpectraNoiseFractionLowIntensityDataToAverage, lblMassSpectraNoiseFractionLowIntensityDataToAverage.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.MassSpectraNoiseMinimumSignalToNoiseRatio = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtMassSpectraNoiseMinimumSignalToNoiseRatio, lblMassSpectraNoiseMinimumSignalToNoiseRatio.Text & " must be a value", blnError)
				If blnError Then Exit Try

				' Similarity Options
				.SimilarIonMZToleranceHalfWidth = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtSimilarIonMZToleranceHalfWidth, lblSimilarIonMZToleranceHalfWidth.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.SimilarIonToleranceHalfWidthMinutes = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtSimilarIonToleranceHalfWidthMinutes, lblSimilarIonTimeToleranceHalfWidth.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.SpectrumSimilarityMinimum = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtSpectrumSimilarityMinimum, lblSpectrumSimilarityMinimum.Text & " must be a value", blnError)
				If blnError Then Exit Try

				' Binning Options
				.BinStartX = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtBinStartX, lblBinStartX.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.BinEndX = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtBinEndX, lblBinEndX.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.BinSize = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtBinSize, lblBinSize.Text & " must be a value", blnError)
				If blnError Then Exit Try
				.MaximumBinCount = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueInt(txtMaximumBinCount, lblMaximumBinCount.Text & " must be an integer value", blnError)
				If blnError Then Exit Try

				.BinnedDataIntensityPrecisionPercent = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtBinnedDataIntensityPrecisionPct, lblBinnedDataIntensityPrecisionPct.Text & " must be a value", blnError)
				If blnError Then Exit Try

				.NormalizeBinnedData = chkBinnedDataNormalize.Checked
				.SumAllIntensitiesForBin = chkBinnedDataSumAllIntensitiesForBin.Checked

				' Spectrum caching options
				.DiskCachingAlwaysDisabled = mCacheOptions.DiskCachingAlwaysDisabled
				.CacheFolderPath = mCacheOptions.FolderPath
				.CacheSpectraToRetainInMemory = mCacheOptions.SpectraToRetainInMemory
				.CacheMinimumFreeMemoryMB = mCacheOptions.MinimumFreeMemoryMB
				.CacheMaximumMemoryUsageMB = mCacheOptions.MaximumMemoryUsageMB

				' Reporter ion options
				.ReporterIonStatsEnabled = chkReporterIonStatsEnabled.Checked

				' Note that this will set .ReporterIonToleranceDa to 0.5
				.ReporterIonMassMode = CType(cboReporterIonMassMode.SelectedIndex, clsMASIC.eReporterIonMassModeConstants)

				' Update .ReporterIonToleranceDa based on txtReporterIonMZToleranceDa
				.ReporterIonToleranceDaDefault = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueDbl(txtReporterIonMZToleranceDa, "", blnError, clsMASIC.REPORTER_ION_TOLERANCE_DA_DEFAULT, False)
				.ReporterIonApplyAbundanceCorrection = chkReporterIonApplyAbundanceCorrection.Checked

				.ReporterIonSaveObservedMasses = chkReporterIonSaveObservedMasses.Checked
				.ReporterIonSaveUncorrectedIntensities = chkReporterIonSaveUncorrectedIntensities.Checked

				' MRM Options
				.WriteMRMDataList = chkMRMWriteDataList.Checked
				.WriteMRMIntensityCrosstab = chkMRMWriteIntensityCrosstab.Checked

				' Custom m/z options
				.LimitSearchToCustomMZList = chkLimitSearchToCustomMZs.Checked
			End With

			' Store the custom M/Z values in mCustomSICList

			strCustomSICFileName = txtCustomSICFileName.Text.Trim
			objMasic.CustomSICListFileName = strCustomSICFileName

			If strCustomSICFileName.Length > 0 Then
				ReDim dblMZList(-1)
				ReDim dblMZToleranceList(-1)
				ReDim sngScanOrAcqTimeList(-1)
				ReDim sngScanOrAcqTimeToleranceList(-1)
				ReDim strScanComments(-1)
			Else

				With mCustomSICValuesDataset.Tables(CUSTOM_SIC_VALUES_DATATABLE)

					intCustomSICListCount = 0
					ReDim dblMZList(.Rows.Count - 1)
					ReDim dblMZToleranceList(.Rows.Count - 1)
					ReDim sngScanOrAcqTimeList(.Rows.Count - 1)
					ReDim sngScanOrAcqTimeToleranceList(.Rows.Count - 1)
					ReDim strScanComments(.Rows.Count - 1)

					For Each myDataRow In .Rows
						With myDataRow
							If IsNumeric(.Item(0)) And IsNumeric(.Item(1)) Then
								dblMZList(intCustomSICListCount) = CDbl(.Item(0))
								dblMZToleranceList(intCustomSICListCount) = CDbl(.Item(1))
								sngScanOrAcqTimeList(intCustomSICListCount) = CSng(.Item(2))
								sngScanOrAcqTimeToleranceList(intCustomSICListCount) = CSng(.Item(3))
								strScanComments(intCustomSICListCount) = SharedVBNetRoutines.VBNetRoutines.CStrSafe(.Item(4))
								intCustomSICListCount += 1
							End If
						End With
					Next myDataRow

					ReDim Preserve dblMZList(intCustomSICListCount - 1)
					ReDim Preserve dblMZToleranceList(intCustomSICListCount - 1)
					ReDim Preserve sngScanOrAcqTimeList(intCustomSICListCount - 1)
					ReDim Preserve sngScanOrAcqTimeToleranceList(intCustomSICListCount - 1)
					ReDim Preserve strScanComments(intCustomSICListCount - 1)

				End With
			End If

			If optCustomSICScanToleranceAbsolute.Checked Then
				eScanType = clsMASIC.eCustomSICScanTypeConstants.Absolute
			ElseIf optCustomSICScanToleranceRelative.Checked Then
				eScanType = clsMASIC.eCustomSICScanTypeConstants.Relative
			ElseIf optCustomSICScanToleranceAcqTime.Checked Then
				eScanType = clsMASIC.eCustomSICScanTypeConstants.AcquisitionTime
			Else
				' Assume absolute
				eScanType = clsMASIC.eCustomSICScanTypeConstants.Absolute
			End If

			sngScanOrAcqTimeTolerance = SharedVBNetRoutines.VBNetRoutines.ParseTextboxValueSng(txtCustomSICScanOrAcqTimeTolerance, lblCustomSICScanTolerance.Text & " must be a value", blnError)
			If blnError Then Exit Try

			objMasic.SetCustomSICListValues(eScanType, objMasic.SICToleranceDa, sngScanOrAcqTimeTolerance, dblMZList, dblMZToleranceList, sngScanOrAcqTimeList, sngScanOrAcqTimeToleranceList, strScanComments)

		Catch ex As Exception
			System.Windows.Forms.MessageBox.Show("Error applying setting to clsMASIC: " & ControlChars.NewLine & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
		End Try

		Return Not blnError

	End Function

	Private Function ValidateSettings(ByRef objMasic As clsMASIC) As Boolean

		Dim eResponse As System.Windows.Forms.DialogResult

		If objMasic.ReporterIonMassMode <> clsMASIC.eReporterIonMassModeConstants.CustomOrNone Then
			If objMasic.ReporterIonMassMode = clsMASIC.eReporterIonMassModeConstants.ITraqEightMZHighRes Then
				' Make sure the tolerance is less than 0.03 Da; if not, warn the user
				If objMasic.ReporterIonToleranceDaDefault > 0.03 Then
					eResponse = System.Windows.Forms.MessageBox.Show("Warning: the Reporter Ion 'm/z Tolerance Half Width' value should be less than 0.03 m/z when using 'iTraq8 for High Res MS/MS' reporter ions.  It is currently " & objMasic.ReporterIonToleranceDaDefault.ToString("0.000") & " m/z.  If using a low resolution instrument, you should choose the 'iTraq 8 for Low Res MS/MS' mode.  Continue anyway?", "Warning", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)

					If eResponse <> Windows.Forms.DialogResult.Yes Then
						Return False
					End If
				End If

			ElseIf objMasic.ReporterIonMassMode = clsMASIC.eReporterIonMassModeConstants.ITraqEightMZLowRes Then
				' Make sure the tolerance is at least 0.1 Da; if not, warn the user
				If objMasic.ReporterIonToleranceDaDefault < 0.1 Then
					eResponse = System.Windows.Forms.MessageBox.Show("Warning: the Reporter Ion 'm/z Tolerance Half Width' value should be at least 0.1 m/z when using 'iTraq8 for Low Res MS/MS' reporter ions.  It is currently " & objMasic.ReporterIonToleranceDaDefault.ToString("0.000") & " m/z. If using a high resolution instrument, you should choose the 'iTraq 8 for High Res MS/MS' mode.  Continue anyway?", "Warning", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)

					If eResponse <> Windows.Forms.DialogResult.Yes Then
						Return False
					End If
				End If

			End If
		End If

		Return True
	End Function

#End Region

#Region "Combobox Handlers"

	Private Sub cboMassSpectraNoiseThresholdMode_SelectedIndexChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cboMassSpectraNoiseThresholdMode.SelectedIndexChanged
		EnableDisableControls()
	End Sub

	Private Sub cboSICNoiseThresholdMode_SelectedIndexChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cboSICNoiseThresholdMode.SelectedIndexChanged
		EnableDisableControls()
	End Sub
#End Region

#Region "Button Handlers"
	Private Sub cmdClearAllRangeFilters_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdClearAllRangeFilters.Click
		ClearAllRangeFilters()
	End Sub

	Private Sub cmdClearCustomSICList_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdClearCustomSICList.Click
		ClearCustomSICList(True)
	End Sub

	Private Sub cmdCustomSICValuesPopulate_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdCustomSICValuesPopulate.Click
		AutoPopulateCustomSICValues(True)
	End Sub

	Private Sub cmdPasteCustomSICList_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdPasteCustomSICList.Click
		PasteCustomSICValues(False)
	End Sub

	Private Sub cmdStartProcessing_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdStartProcessing.Click
		ProcessFileUsingMASIC()
	End Sub

	Private Sub cmdSelectDatasetLookupFile_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSelectDatasetLookupFile.Click
		SelectDatasetLookupFile()
	End Sub

	Private Sub cmdSelectCustomSICFile_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSelectCustomSICFile.Click
		SelectCustomSICFile()
	End Sub

	Private Sub cmdSelectFile_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSelectFile.Click
		SelectInputFile()
	End Sub

	Private Sub cmdSelectOutputFolder_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSelectOutputFolder.Click
		SelectOutputFolder()
	End Sub

	Private Sub cmdSetConnectionStringToPNNLServer_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles cmdSetConnectionStringToPNNLServer.Click
		SetConnectionStringToPNNLServer()
	End Sub
#End Region

#Region "Checkbox Events"
	Private Sub chkExportRawDataOnly_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkExportRawDataOnly.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkSkipMSMSProcessing_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkSkipMSMSProcessing.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkSkipSICAndRawDataProcessing_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkSkipSICAndRawDataProcessing.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkExportRawSpectraData_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkExportRawSpectraData.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkExportRawDataIncludeMSMS_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkExportRawDataIncludeMSMS.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkSaveExtendedStatsFile_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkSaveExtendedStatsFile.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub chkReporterIonStatsEnabled_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkReporterIonStatsEnabled.CheckedChanged
		AutoToggleReporterIonStatsMode()
	End Sub

	Private Sub chkSaveExtendedStatsFileIncludeStatusLog_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkSaveExtendedStatsFileIncludeStatusLog.CheckedChanged
		EnableDisableControls()
	End Sub

#End Region

#Region "Radio Button Events"
	Private Sub optUseButterworthSmooth_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles optUseButterworthSmooth.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub optUseSavitzkyGolaySmooth_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles optUseSavitzkyGolaySmooth.CheckedChanged
		EnableDisableControls()
	End Sub

	Private Sub optCustomSICScanToleranceAbsolute_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles optCustomSICScanToleranceAbsolute.CheckedChanged
		UpdateCustomSICDataGridTableStyle()
	End Sub

	Private Sub optCustomSICScanToleranceRelative_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles optCustomSICScanToleranceRelative.CheckedChanged
		UpdateCustomSICDataGridTableStyle()
	End Sub

	Private Sub optCustomSICScanToleranceAcqTime_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles optCustomSICScanToleranceAcqTime.CheckedChanged
		UpdateCustomSICDataGridTableStyle()
	End Sub
#End Region

#Region "Textbox Events"
    Private Sub txtMassSpectraNoiseThresholdIntensity_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMassSpectraNoiseThresholdIntensity.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMassSpectraNoiseThresholdIntensity, e, True, True)
    End Sub

    Private Sub txtMassSpectraNoiseFractionLowIntensityDataToAverage_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMassSpectraNoiseFractionLowIntensityDataToAverage.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMassSpectraNoiseFractionLowIntensityDataToAverage, e, True, True)
    End Sub

    Private Sub txtBinnedDataIntensityPrecisionPct_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtBinnedDataIntensityPrecisionPct.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtBinnedDataIntensityPrecisionPct, e, True, True)
    End Sub

    Private Sub txtBinSize_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtBinSize.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtBinSize, e, True, True)
    End Sub

    Private Sub txtBinStartX_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtBinStartX.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtBinStartX, e, True, True)
    End Sub

    Private Sub txtBinEndX_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtBinEndX.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtBinEndX, e, True, True)
    End Sub

    Private Sub txtButterworthSamplingFrequency_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtButterworthSamplingFrequency.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtButterworthSamplingFrequency, e, True, True)
    End Sub

    Private Sub txtButterworthSamplingFrequency_Validating(ByVal sender As Object, ByVal e As System.ComponentModel.CancelEventArgs) Handles txtButterworthSamplingFrequency.Validating
        SharedVBNetRoutines.VBNetRoutines.ValidateTextboxSng(txtButterworthSamplingFrequency, 0.01, 0.99, 0.25)
    End Sub

    Private Sub txtCustomSICFileDescription_KeyDown(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyEventArgs) Handles txtCustomSICFileDescription.KeyDown
        If e.KeyCode = Keys.A AndAlso e.Control = True Then
            txtCustomSICFileDescription.SelectAll()
        End If
    End Sub

    Private Sub txtCustomSICFileName_TextChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles txtCustomSICFileName.TextChanged
        EnableDisableCustomSICValueGrid()
    End Sub

    Private Sub txtCustomSICScanOrAcqTimeTolerance_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtCustomSICScanOrAcqTimeTolerance.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtCustomSICScanOrAcqTimeTolerance, e, True, True)
    End Sub

    Private Sub txtDatasetNumber_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtDatasetNumber.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtDatasetNumber, e, True, False)
    End Sub

    Private Sub txtExportRawDataIntensityMinimum_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtExportRawDataIntensityMinimum.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtExportRawDataIntensityMinimum, e, True, True)
    End Sub

    Private Sub txtExportRawDataMaxIonCountPerScan_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtExportRawDataMaxIonCountPerScan.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtExportRawDataMaxIonCountPerScan, e)
    End Sub

    Private Sub txtExportRawDataSignalToNoiseRatioMinimum_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtExportRawDataSignalToNoiseRatioMinimum.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtExportRawDataSignalToNoiseRatioMinimum, e, True, True)
    End Sub

    Private Sub txtInitialPeakWidthScansMaximum_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtInitialPeakWidthScansMaximum.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtInitialPeakWidthScansMaximum, e)
    End Sub

    Private Sub txtInitialPeakWidthScansScaler_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtInitialPeakWidthScansScaler.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtInitialPeakWidthScansScaler, e)
    End Sub

    Private Sub txtIntensityThresholdAbsoluteMinimum_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtIntensityThresholdAbsoluteMinimum.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtIntensityThresholdAbsoluteMinimum, e, True, True)
    End Sub

    Private Sub txtIntensityThresholdFractionMax_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtIntensityThresholdFractionMax.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtIntensityThresholdFractionMax, e, True, True)
    End Sub

    Private Sub txtMaxAllowedUpwardSpikeFractionMax_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMaxAllowedUpwardSpikeFractionMax.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMaxAllowedUpwardSpikeFractionMax, e, True, True)
    End Sub

    Private Sub txtMaxDistanceScansNoOverlap_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMaxDistanceScansNoOverlap.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMaxDistanceScansNoOverlap, e)
    End Sub

    Private Sub txtMaximumBinCount_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMaximumBinCount.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMaximumBinCount, e)
    End Sub

    Private Sub txtMaxPeakWidthMinutesBackward_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMaxPeakWidthMinutesBackward.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMaxPeakWidthMinutesBackward, e, True, True)
    End Sub

    Private Sub txtMaxPeakWidthMinutesForward_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtMaxPeakWidthMinutesForward.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtMaxPeakWidthMinutesForward, e, True, True)
    End Sub

    Private Sub txtSICNoiseThresholdIntensity_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSICNoiseThresholdIntensity.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSICNoiseThresholdIntensity, e, True, True)
    End Sub

    Private Sub txtSICNoiseFractionLowIntensityDataToAverage_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSICNoiseFractionLowIntensityDataToAverage.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSICNoiseFractionLowIntensityDataToAverage, e, True, True)
    End Sub

    Private Sub txtSavitzkyGolayFilterOrder_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSavitzkyGolayFilterOrder.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSavitzkyGolayFilterOrder, e)
    End Sub

    Private Sub txtSavitzkyGolayFilterOrder_Validating(ByVal sender As Object, ByVal e As System.ComponentModel.CancelEventArgs) Handles txtSavitzkyGolayFilterOrder.Validating
        SharedVBNetRoutines.VBNetRoutines.ValidateTextboxInt(txtSavitzkyGolayFilterOrder, 0, 20, 0)
    End Sub

    Private Sub txtSICTolerance_KeyPress(ByVal sender As Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSICTolerance.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSICTolerance, e, True, True)
    End Sub

    Private Sub txtSimilarIonMZToleranceHalfWidth_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSimilarIonMZToleranceHalfWidth.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSimilarIonMZToleranceHalfWidth, e, True, True)
    End Sub

    Private Sub txtSimilarIonToleranceHalfWidthMinutes_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSimilarIonToleranceHalfWidthMinutes.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSimilarIonToleranceHalfWidthMinutes, e, True, True)
    End Sub

    Private Sub txtSpectrumSimilarityMinimum_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtSpectrumSimilarityMinimum.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtSpectrumSimilarityMinimum, e, True, True)
    End Sub

    Private Sub txtScanEnd_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtScanEnd.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtScanEnd, e, True, False)
    End Sub

    Private Sub txtScanStart_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtScanStart.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtScanStart, e, True, False)
    End Sub

    Private Sub txtTimeEnd_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtTimeEnd.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtTimeEnd, e, True, True)
    End Sub

    Private Sub txtTimeStart_KeyPress(ByVal sender As System.Object, ByVal e As System.Windows.Forms.KeyPressEventArgs) Handles txtTimeStart.KeyPress
        SharedVBNetRoutines.VBNetRoutines.TextBoxKeyPressHandler(txtTimeStart, e, True, True)
    End Sub
#End Region

#Region "Menu Handlers"
    Private Sub mnuFileSelectInputFile_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuFileSelectInputFile.Click
        SelectInputFile()
    End Sub

    Private Sub mnuFileSelectOutputFolder_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuFileSelectOutputFolder.Click
        SelectOutputFolder()
    End Sub

    Private Sub mnuFileLoadOptions_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuFileLoadOptions.Click
        IniFileLoadOptions(False)
    End Sub

    Private Sub mnuFileSaveOptions_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuFileSaveOptions.Click
        IniFileSaveOptions()
    End Sub

    Private Sub mnuFileExit_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuFileExit.Click
        Me.Close()
    End Sub

    Private Sub mnuEditProcessFile_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuEditProcessFile.Click
        ProcessFileUsingMASIC()
    End Sub

    Private Sub mnuEditResetOptions_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuEditResetOptions.Click
        ResetToDefaults(True)
    End Sub

    Private Sub mnuEditSaveDefaultOptions_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuEditSaveDefaultOptions.Click
        IniFileSaveDefaultOptions()
    End Sub

    Private Sub mnuHelpAbout_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles mnuHelpAbout.Click
        ShowAboutBox()
    End Sub

#End Region

#Region "Form and Masic Class Events"
    Private Sub frmMain_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load
        ' Note that InitializeControls() is called in Sub New()
    End Sub

    Private Sub frmMain_Closing(ByVal sender As Object, ByVal e As System.ComponentModel.CancelEventArgs) Handles MyBase.Closing
        IniFileSaveOptions(GetSettingsFilePath(), True)
        '' DeleteCachedSpectrumFiles()
    End Sub

    Private Sub mMASIC_ProgressChanged(ByVal taskDescription As String, ByVal percentComplete As Single) Handles mMasic.ProgressChanged
        If Not mProgressForm Is Nothing Then
            mProgressForm.UpdateCurrentTask(mMasic.ProgressStepDescription)
            mProgressForm.UpdateProgressBar(percentComplete)
            If mProgressForm.KeyPressAbortProcess Then
                mMasic.AbortProcessingNow()
            End If
            Application.DoEvents()
        End If
    End Sub

    Private Sub mMASIC_ProgressResetKeypressAbort() Handles mMasic.ProgressResetKeypressAbort
        If Not mProgressForm Is Nothing Then
            mProgressForm.ResetKeyPressAbortProcess()
        End If
    End Sub

    Private Sub mMASIC_ProgressSubtaskChanged() Handles mMasic.ProgressSubtaskChanged
        If Not mProgressForm Is Nothing Then
            mProgressForm.UpdateCurrentSubTask(mMasic.SubtaskDescription)
            mProgressForm.UpdateSubtaskProgressBar(mMasic.SubtaskProgressPercentComplete)
            If mProgressForm.KeyPressAbortProcess Then
                mMasic.AbortProcessingNow()
            End If
            Application.DoEvents()
        End If
    End Sub
#End Region

	Private Sub cboReporterIonMassMode_SelectedIndexChanged(sender As System.Object, e As System.EventArgs) Handles cboReporterIonMassMode.SelectedIndexChanged
		AutoToggleReporterIonStatsEnabled()
	End Sub
End Class