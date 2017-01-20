Option Strict On

' See clsMASIC for a program description
'
' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Program started October 11, 2003
' Copyright 2005, Battelle Memorial Institute.  All Rights Reserved.

' E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
' Website: http://omics.pnl.gov/ or http://www.sysbio.org/resources/staff/ or http://panomics.pnnl.gov/
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

Public Module modMain

    Public Const PROGRAM_DATE As String = "January 18, 2017"

    Private mInputFilePath As String
    Private mOutputFolderPath As String             ' Optional
    Private mParameterFilePath As String            ' Optional
    Private mOutputFolderAlternatePath As String                ' Optional
    Private mRecreateFolderHierarchyInAlternatePath As Boolean  ' Optional

    Private mDatasetLookupFilePath As String        ' Optional
    Private mDatasetNumber As Integer

    Private mRecurseFolders As Boolean
    Private mRecurseFoldersMaxLevels As Integer

    Private mLogMessagesToFile As Boolean
    Private mLogFilePath As String = String.Empty
    Private mLogFolderPath As String = String.Empty

    Private mMASICStatusFilename As String = String.Empty
    Private mQuietMode As Boolean

    Private mTestMode As Boolean

    Private WithEvents mMASIC As clsMASIC
    Private mProgressForm As ProgressFormNET.frmProgress

    Private mLastProgressReportTime As DateTime
    Private mLastProgressReportValue As Integer
    
    Private Function GetMemoryUsage() As Single

        Dim sngMemoryUsageMB As Single

        ' Obtain a handle to the current process
        Dim objProcess As System.Diagnostics.Process
        objProcess = System.Diagnostics.Process.GetCurrentProcess()

        ' The WorkingSet is the total physical memory usage 
        sngMemoryUsageMB = CSng(objProcess.WorkingSet64() / 1024 / 1024)
        Return sngMemoryUsageMB

    End Function

    ''Private Sub InitializeTraceLogFile(strUserDefinedLogFilePath As String)
    ''    Dim strTraceFilePath As String

    ''    Static blnTraceFileEnabled As Boolean

    ''    If Not blnTraceFileEnabled Then
    ''        Try
    ''            If strUserDefinedLogFilePath Is Nothing Then strUserDefinedLogFilePath = String.Empty

    ''            If strUserDefinedLogFilePath.Length = 0 Then
    ''                strTraceFilePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
    ''                strTraceFilePath = Path.Combine(strTraceFilePath, "MASIC_Log_" & System.DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss") & ".txt")
    ''            Else
    ''                strTraceFilePath = String.Copy(strUserDefinedLogFilePath)
    ''            End If

    ''            Trace.Listeners.Add(New TextWriterTraceListener(strTraceFilePath))
    ''            Trace.AutoFlush = True

    ''            blnTraceFileEnabled = True
    ''        Catch ex As Exception
    ''            If strUserDefinedLogFilePath.Length > 0 Then
    ''                ' Error using the user-defined path
    ''                ' Call this function using a blank string so that a default file is created
    ''                InitializeTraceLogFile(String.Empty)
    ''            End If

    ''        End Try

    ''    End If

    ''End Sub

    Public Function Main() As Integer
        ' Returns 0 if no error, error code if an error

        Dim objParseCommandLine As New clsParseCommandLine
        Dim blnProceed As Boolean

        mInputFilePath = String.Empty
        mOutputFolderPath = String.Empty
        mParameterFilePath = String.Empty

        mRecurseFolders = False
        mRecurseFoldersMaxLevels = 0

        mQuietMode = False
        mTestMode = False
        mLogMessagesToFile = False
        mLogFilePath = String.Empty

        Try
            blnProceed = False
            If objParseCommandLine.ParseCommandLine Then
                If SetOptionsUsingCommandLineParameters(objParseCommandLine) Then blnProceed = True
            End If

            If (objParseCommandLine.ParameterCount + objParseCommandLine.NonSwitchParameterCount = 0) And Not objParseCommandLine.NeedToShowHelp Then
                ShowGUI()
                Return 0
            End If

            If mTestMode Then
                RunTests()
                Return 0
            End If

            If Not blnProceed OrElse objParseCommandLine.NeedToShowHelp OrElse mInputFilePath.Length = 0 Then
                ShowProgramHelp()
                Return -1
            End If

            mMASIC = New clsMASIC()

            mMASIC.DatasetLookupFilePath = mDatasetLookupFilePath
            mMASIC.DatasetNumber = mDatasetNumber

            If Not mMASICStatusFilename Is Nothing AndAlso mMASICStatusFilename.Length > 0 Then
                mMASIC.MASICStatusFilename = mMASICStatusFilename
            End If

            mMASIC.ShowMessages = Not mQuietMode
            mMASIC.LogMessagesToFile = mLogMessagesToFile
            mMASIC.LogFilePath = mLogFilePath
            mMASIC.LogFolderPath = mLogFolderPath

            If mMASIC.ShowMessages Then
                mProgressForm = New ProgressFormNET.frmProgress

                mProgressForm.InitializeProgressForm("Parsing " & Path.GetFileName(mInputFilePath), 0, 100, False, True)
                mProgressForm.InitializeSubtask("", 0, 100, False)
                mProgressForm.ResetKeyPressAbortProcess()
                mProgressForm.Show()
                Application.DoEvents()

            End If

            Dim intReturnCode As Integer

            If mRecurseFolders Then
                If mMASIC.ProcessFilesAndRecurseFolders(mInputFilePath, mOutputFolderPath, mOutputFolderAlternatePath, mRecreateFolderHierarchyInAlternatePath, mParameterFilePath, mRecurseFoldersMaxLevels) Then
                    intReturnCode = 0
                Else
                    intReturnCode = mMASIC.ErrorCode
                End If
            Else
                If mMASIC.ProcessFilesWildcard(mInputFilePath, mOutputFolderPath, mParameterFilePath) Then
                    intReturnCode = 0
                Else
                    intReturnCode = mMASIC.ErrorCode
                    If intReturnCode <> 0 AndAlso Not mQuietMode Then
                        Console.WriteLine("Error while processing: " & mMASIC.GetErrorMessage())
                    End If
                End If
            End If

            Return 0

        Catch ex As Exception
            ShowErrorMessage("Error occurred in modMain->Main: " & Environment.NewLine & ex.Message)
            Return -1
        Finally
            If Not mProgressForm Is Nothing Then
                mProgressForm.HideForm()
                mProgressForm = Nothing
            End If
        End Try

        ''Trace.Close()


    End Function

    Private Sub RunTests()
        Try

            clsTests.TestValueToString()

            Console.WriteLine()

        Catch ex As Exception
            ShowErrorMessage("Error occurred in modMain->RunTests: " & Environment.NewLine & ex.Message)
        End Try

    End Sub

    Private Sub DisplayProgressPercent(intPercentComplete As Integer, blnAddCarriageReturn As Boolean)
        If blnAddCarriageReturn Then
            Console.WriteLine()
        End If
        If intPercentComplete > 100 Then intPercentComplete = 100
        Console.Write("Processing: " & intPercentComplete.ToString() & "% ")
        If blnAddCarriageReturn Then
            Console.WriteLine()
        End If
    End Sub

    Private Function GetAppVersion() As String
        Return clsProcessFilesBaseClass.GetAppVersion(PROGRAM_DATE)
    End Function


    Private Function SetOptionsUsingCommandLineParameters(objParseCommandLine As clsParseCommandLine) As Boolean
        ' Returns True if no problems; otherwise, returns false

        Dim strValue As String = String.Empty
        Dim lstValidParameters = New List(Of String) From {"I", "O", "P", "D", "S", "A", "R", "L", "SF", "Test", "LogFolder", "Q"}
        Dim intValue As Integer

        Try
            ' Make sure no invalid parameters are present
            If objParseCommandLine.InvalidParametersPresent(lstValidParameters) Then
                ShowErrorMessage("Invalid commmand line parameters",
                  (From item In objParseCommandLine.InvalidParameters(lstValidParameters) Select "/" + item).ToList())
                Return False
            Else

                ' Query objParseCommandLine to see if various parameters are present
                With objParseCommandLine
                    If .RetrieveValueForParameter("I", strValue) Then
                        mInputFilePath = strValue
                    ElseIf .NonSwitchParameterCount > 0 Then
                        ' Treat the first non-switch parameter as the input file
                        mInputFilePath = .RetrieveNonSwitchParameter(0)
                    End If

                    If .RetrieveValueForParameter("O", strValue) Then mOutputFolderPath = strValue
                    If .RetrieveValueForParameter("P", strValue) Then mParameterFilePath = strValue
                    If .RetrieveValueForParameter("D", strValue) Then
                        If Not IsNumeric(strValue) AndAlso Not strValue Is Nothing Then
                            ' Assume the user specified a dataset number lookup file (comma or tab delimited file specifying the dataset number for each input file)
                            mDatasetLookupFilePath = strValue
                            mDatasetNumber = 0
                        Else
                            mDatasetNumber = CInt(strValue)
                        End If
                    End If

                    If .RetrieveValueForParameter("S", strValue) Then
                        mRecurseFolders = True
                        If Integer.TryParse(strValue, intValue) Then
                            mRecurseFoldersMaxLevels = intValue
                        End If
                    End If
                    If .RetrieveValueForParameter("A", strValue) Then mOutputFolderAlternatePath = strValue
                    If .IsParameterPresent("R") Then mRecreateFolderHierarchyInAlternatePath = True

                    If .RetrieveValueForParameter("L", strValue) Then
                        mLogMessagesToFile = True
                        If Not String.IsNullOrEmpty(strValue) Then
                            mLogFilePath = strValue.Trim(""""c)
                        End If
                    End If

                    If .RetrieveValueForParameter("SF", strValue) Then
                        mMASICStatusFilename = strValue
                    End If

                    If .IsParameterPresent("Test") Then
                        mTestMode = True
                    End If

                    If .RetrieveValueForParameter("LogFolder", strValue) Then
                        mLogMessagesToFile = True
                        If Not String.IsNullOrEmpty(strValue) Then
                            mLogFolderPath = strValue
                        End If
                    End If
                    If .IsParameterPresent("Q") Then mQuietMode = True
                End With

                Return True
            End If

        Catch ex As Exception
            ShowErrorMessage("Error parsing the command line parameters: " & Environment.NewLine & ex.Message)
        End Try

        Return False

    End Function

    Private Sub ShowErrorMessage(strMessage As String)
        Dim strSeparator = "------------------------------------------------------------------------------"

        Console.WriteLine()
        Console.WriteLine(strSeparator)
        Console.WriteLine(strMessage)
        Console.WriteLine(strSeparator)
        Console.WriteLine()

        WriteToErrorStream(strMessage)
    End Sub

    Private Sub ShowErrorMessage(strTitle As String, items As List(Of String))
        Dim strSeparator = "------------------------------------------------------------------------------"
        Dim strMessage As String

        Console.WriteLine()
        Console.WriteLine(strSeparator)
        Console.WriteLine(strTitle)
        strMessage = strTitle & ":"

        For Each item As String In items
            Console.WriteLine("   " + item)
            strMessage &= " " & item
        Next
        Console.WriteLine(strSeparator)
        Console.WriteLine()

        WriteToErrorStream(strMessage)
    End Sub

    Public Sub ShowGUI()
        Dim objFormMain As frmMain

        System.Windows.Forms.Application.EnableVisualStyles()
        System.Windows.Forms.Application.DoEvents()

        objFormMain = New frmMain

        ' The following call is needed because the .ShowDialog() call is inexplicably increasing the size of the form
        objFormMain.SetHeightAdjustForce(objFormMain.Height)

        objFormMain.ShowDialog()

    End Sub

    Private Sub ShowProgramHelp()

        Try

            Console.WriteLine("This program will read a Finnigan LCQ .RAW file or Agilent LC/MSD .CDF/.MGF file combo and create a selected ion chromatogram (SIC) for each parent ion.")
            Console.WriteLine()

            Console.WriteLine("Program syntax:" & Environment.NewLine & Path.GetFileName(clsProcessFilesBaseClass.GetAppPath()))
            Console.WriteLine(" /I:InputFilePath.raw [/O:OutputFolderPath]")
            Console.WriteLine(" [/P:ParamFilePath] [/D:DatasetNumber or DatasetLookupFilePath] ")
            Console.WriteLine(" [/S:[MaxLevel]] [/A:AlternateOutputFolderPath] [/R]")
            Console.WriteLine(" [/L:[LogFilePath]] [/LogFolder:LogFolderPath] [/SF:StatusFileName] [/Q]")
            Console.WriteLine()

            Console.WriteLine("The input file path can contain the wildcard character *")
            Console.WriteLine("The output folder name is optional.  If omitted, the output files will be created in the same folder as the input file.  If included, then a subfolder is created with the name OutputFolderName.")
            Console.WriteLine("The param file switch is optional.  If supplied, it should point to a valid MASIC XML parameter file.  If omitted, defaults are used.")
            Console.WriteLine()
            Console.WriteLine("The /D switch can be used to specify the dataset number of the input file; if omitted, 0 will be used")
            Console.WriteLine("Alternatively, a lookup file can be specified with the /D switch (useful if processing multiple files using * or /S)")
            Console.WriteLine()
            Console.WriteLine("Use /S to process all valid files in the input folder and subfolders. Include a number after /S (like /S:2) to limit the level of subfolders to examine.")
            Console.WriteLine("When using /S, you can redirect the output of the results using /A.")
            Console.WriteLine("When using /S, you can use /R to re-create the input folder hierarchy in the alternate output folder (if defined).")
            Console.WriteLine()
            Console.WriteLine("Use /L to specify that a log file should be created.  Use /L:LogFilePath to specify the name (or full path) for the log file.")
            Console.WriteLine("Use /SF to specify the name to use for the Masic Status file (default is " & clsMASIC.DEFAULT_MASIC_STATUS_FILE_NAME & ").")
            Console.WriteLine("The optional /Q switch will suppress all error messages.")
            Console.WriteLine()

            Console.WriteLine("Program written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2003")
            Console.WriteLine("Version: " & GetAppVersion())

            Console.WriteLine()

            Console.WriteLine("E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com")
            Console.WriteLine("Website: http://omics.pnl.gov/ or http://panomics.pnnl.gov/")
            Console.WriteLine()

            ' Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
            Threading.Thread.Sleep(750)

        Catch ex As Exception
            ShowErrorMessage("Error displaying the program syntax: " & ex.Message)
        End Try

    End Sub

    Private Sub WriteToErrorStream(strErrorMessage As String)
        Try
            Using swErrorStream = New StreamWriter(Console.OpenStandardError())
                swErrorStream.WriteLine(strErrorMessage)
            End Using
        Catch ex As Exception
            ' Ignore errors here
        End Try
    End Sub

    Private Sub mMASIC_ProgressChanged(taskDescription As String, percentComplete As Single) Handles mMASIC.ProgressChanged
        Const PERCENT_REPORT_INTERVAL = 25
        Const PROGRESS_DOT_INTERVAL_MSEC = 250

        If Not mProgressForm Is Nothing Then
            mProgressForm.UpdateCurrentTask(mMASIC.ProgressStepDescription)
            mProgressForm.UpdateProgressBar(percentComplete)
            If mProgressForm.KeyPressAbortProcess Then
                mMASIC.AbortProcessingNow()
            End If
            Application.DoEvents()

        Else

            If percentComplete >= mLastProgressReportValue Then
                If mLastProgressReportValue > 0 Then
                    Console.WriteLine()
                End If
                DisplayProgressPercent(mLastProgressReportValue, False)
                mLastProgressReportValue += PERCENT_REPORT_INTERVAL
                mLastProgressReportTime = DateTime.UtcNow
            Else
                If DateTime.UtcNow.Subtract(mLastProgressReportTime).TotalMilliseconds > PROGRESS_DOT_INTERVAL_MSEC Then
                    mLastProgressReportTime = DateTime.UtcNow
                    Console.Write(".")
                End If
            End If
        End If

    End Sub

    Private Sub mMASIC_ProgressResetKeypressAbort() Handles mMASIC.ProgressResetKeypressAbort
        If Not mProgressForm Is Nothing Then
            mProgressForm.ResetKeyPressAbortProcess()
        End If
    End Sub

    Private Sub mMASIC_ProgressSubtaskChanged() Handles mMASIC.ProgressSubtaskChanged
        If Not mProgressForm Is Nothing Then
            mProgressForm.UpdateCurrentSubTask(mMASIC.SubtaskDescription)
            mProgressForm.UpdateSubtaskProgressBar(mMASIC.SubtaskProgressPercentComplete)
            If mProgressForm.KeyPressAbortProcess Then
                mMASIC.AbortProcessingNow()
            End If
            Application.DoEvents()
        End If
    End Sub

End Module
