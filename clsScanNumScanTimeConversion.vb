﻿Public Class clsScanNumScanTimeConversion
    Inherits clsEventNotifier

    ''' <summary>
    ''' Returns the index of the scan closest to sngScanOrAcqTime (searching both Survey and Frag Scans using the MasterScanList)
    ''' </summary>
    ''' <param name="scanList"></param>
    ''' <param name="sngScanOrAcqTime">can be absolute, relative, or AcquisitionTime</param>
    ''' <param name="eScanType">Specifies what type of value value sngScanOrAcqTime is; 0=absolute, 1=relative, 2=acquisition time (aka elution time)</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function FindNearestScanNumIndex(
      scanList As clsScanList,
      sngScanOrAcqTime As Single,
      eScanType As clsCustomSICList.eCustomSICScanTypeConstants) As Integer

        Dim intAbsoluteScanNumber As Integer
        Dim intScanIndexMatch As Integer

        Try
            If eScanType = clsCustomSICList.eCustomSICScanTypeConstants.Absolute Or eScanType = clsCustomSICList.eCustomSICScanTypeConstants.Relative Then
                intAbsoluteScanNumber = ScanOrAcqTimeToAbsolute(scanList, sngScanOrAcqTime, eScanType, False)
                intScanIndexMatch = clsBinarySearch.BinarySearchFindNearest(scanList.MasterScanNumList, intAbsoluteScanNumber, scanList.MasterScanOrderCount, clsBinarySearch.eMissingDataModeConstants.ReturnClosestPoint)
            Else
                ' eScanType = eCustomSICScanTypeConstants.AcquisitionTime
                ' Find the closest match in scanList.MasterScanTimeList
                intScanIndexMatch = clsBinarySearch.BinarySearchFindNearest(scanList.MasterScanTimeList, sngScanOrAcqTime, scanList.MasterScanOrderCount, clsBinarySearch.eMissingDataModeConstants.ReturnClosestPoint)
            End If

        Catch ex As Exception
            ReportError("FindNearestScanNumIndex", "Error in FindNearestScanNumIndex", ex, True, False)
            intScanIndexMatch = 0
        End Try

        Return intScanIndexMatch
    End Function

    Public Function FindNearestSurveyScanIndex(
      scanList As clsScanList,
      sngScanOrAcqTime As Single,
      eScanType As clsCustomSICList.eCustomSICScanTypeConstants) As Integer

        ' Finds the index of the survey scan closest to sngScanOrAcqTime
        ' Note that sngScanOrAcqTime can be absolute, relative, or AcquisitionTime; eScanType specifies which it is

        Dim intIndex As Integer
        Dim intScanNumberToFind As Integer
        Dim intSurveyScanIndexMatch As Integer


        Try
            intSurveyScanIndexMatch = -1
            intScanNumberToFind = ScanOrAcqTimeToAbsolute(scanList, sngScanOrAcqTime, eScanType, False)
            For intIndex = 0 To scanList.SurveyScans.Count - 1
                If scanList.SurveyScans(intIndex).ScanNumber >= intScanNumberToFind Then
                    intSurveyScanIndexMatch = intIndex
                    If scanList.SurveyScans(intIndex).ScanNumber <> intScanNumberToFind AndAlso intIndex < scanList.SurveyScans.Count - 1 Then
                        ' Didn't find an exact match; determine which survey scan is closer
                        If Math.Abs(scanList.SurveyScans(intIndex + 1).ScanNumber - intScanNumberToFind) <
                           Math.Abs(scanList.SurveyScans(intIndex).ScanNumber - intScanNumberToFind) Then
                            intSurveyScanIndexMatch += 1
                        End If
                    End If
                    Exit For
                End If
            Next intIndex

            If intSurveyScanIndexMatch < 0 Then
                ' Match not found; return either the first or the last survey scan
                If scanList.SurveyScans.Count > 0 Then
                    intSurveyScanIndexMatch = scanList.SurveyScans.Count - 1
                Else
                    intSurveyScanIndexMatch = 0
                End If
            End If
        Catch ex As Exception
            ReportError("FindNearestSurveyScanIndex", "Error in FindNearestSurveyScanIndex", ex, True, False)
            intSurveyScanIndexMatch = 0
        End Try

        Return intSurveyScanIndexMatch

    End Function

    ''' <summary>
    ''' Converts a scan number of acquisition time to an actual scan number
    ''' </summary>
    ''' <param name="scanList"></param>
    ''' <param name="sngScanOrAcqTime">Value to convert</param>
    ''' <param name="eScanType">Type of the value to convert; 0=Absolute, 1=Relative, 2=Acquisition Time (aka elution time)</param>
    ''' <param name="blnConvertingRangeOrTolerance">True when converting a range</param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function ScanOrAcqTimeToAbsolute(
      scanList As clsScanList,
      sngScanOrAcqTime As Single,
      eScanType As clsCustomSICList.eCustomSICScanTypeConstants,
      blnConvertingRangeOrTolerance As Boolean) As Integer


        Dim intTotalScanRange As Integer
        Dim intAbsoluteScanNumber As Integer
        Dim intMasterScanIndex As Integer

        Dim sngTotalRunTime As Single
        Dim sngRelativeTime As Single

        Try
            Select Case eScanType
                Case clsCustomSICList.eCustomSICScanTypeConstants.Absolute
                    ' sngScanOrAcqTime is an absolute scan number (or range of scan numbers)
                    ' No conversion needed; simply return the value
                    intAbsoluteScanNumber = CInt(sngScanOrAcqTime)

                Case clsCustomSICList.eCustomSICScanTypeConstants.Relative
                    ' sngScanOrAcqTime is a fraction of the total number of scans (for example, 0.5)

                    ' Use the total range of scan numbers
                    With scanList
                        If .MasterScanOrderCount > 0 Then
                            intTotalScanRange = .MasterScanNumList(.MasterScanOrderCount - 1) - .MasterScanNumList(0)

                            intAbsoluteScanNumber = CInt(sngScanOrAcqTime * intTotalScanRange + .MasterScanNumList(0))
                        Else
                            intAbsoluteScanNumber = 0
                        End If
                    End With

                Case clsCustomSICList.eCustomSICScanTypeConstants.AcquisitionTime
                    ' sngScanOrAcqTime is an elution time value
                    ' If blnConvertingRangeOrTolerance = False, then look for the scan that is nearest to sngScanOrAcqTime
                    ' If blnConvertingRangeOrTolerance = True, then Convert sngScanOrAcqTime to a relative scan range and then
                    '   call this function again with that relative time

                    If blnConvertingRangeOrTolerance Then
                        With scanList
                            sngTotalRunTime = .MasterScanTimeList(.MasterScanOrderCount - 1) - .MasterScanTimeList(0)
                            If sngTotalRunTime < 0.1 Then
                                sngTotalRunTime = 1
                            End If

                            sngRelativeTime = sngScanOrAcqTime / sngTotalRunTime
                        End With

                        intAbsoluteScanNumber = ScanOrAcqTimeToAbsolute(scanList, sngRelativeTime, clsCustomSICList.eCustomSICScanTypeConstants.Relative, True)
                    Else
                        intMasterScanIndex = FindNearestScanNumIndex(scanList, sngScanOrAcqTime, eScanType)
                        If intMasterScanIndex >= 0 AndAlso scanList.MasterScanOrderCount > 0 Then
                            intAbsoluteScanNumber = scanList.MasterScanNumList(intMasterScanIndex)
                        End If
                    End If


                Case Else
                    ' Unknown type; assume absolute scan number
                    intAbsoluteScanNumber = CInt(sngScanOrAcqTime)
            End Select


        Catch ex As Exception
            ReportError("ScanOrAcqTimeToAbsolute", "Error in clsMasic->ScanOrAcqTimeToAbsolute ", ex, True, False)
            intAbsoluteScanNumber = 0
        End Try

        Return intAbsoluteScanNumber

    End Function

    Public Function ScanOrAcqTimeToScanTime(
      scanList As clsScanList,
      sngScanOrAcqTime As Single,
      eScanType As clsCustomSICList.eCustomSICScanTypeConstants,
      blnConvertingRangeOrTolerance As Boolean) As Single

        Dim intMasterScanIndex As Integer

        Dim sngTotalRunTime As Single
        Dim sngRelativeTime As Single

        Dim sngComputedScanTime As Single

        Try
            Select Case eScanType
                Case clsCustomSICList.eCustomSICScanTypeConstants.Absolute
                    ' sngScanOrAcqTime is an absolute scan number (or range of scan numbers)

                    ' If blnConvertingRangeOrTolerance = False, then look for the scan that is nearest to sngScanOrAcqTime
                    ' If blnConvertingRangeOrTolerance = True, then Convert sngScanOrAcqTime to a relative scan range and then
                    '   call this function again with that relative time

                    If blnConvertingRangeOrTolerance Then
                        With scanList
                            Dim intTotalScans As Integer
                            intTotalScans = .MasterScanNumList(.MasterScanOrderCount - 1) - .MasterScanNumList(0)
                            If intTotalScans < 1 Then
                                intTotalScans = 1
                            End If

                            sngRelativeTime = sngScanOrAcqTime / intTotalScans
                        End With

                        sngComputedScanTime = ScanOrAcqTimeToScanTime(scanList, sngRelativeTime, clsCustomSICList.eCustomSICScanTypeConstants.Relative, True)
                    Else
                        intMasterScanIndex = FindNearestScanNumIndex(scanList, sngScanOrAcqTime, eScanType)
                        If intMasterScanIndex >= 0 AndAlso scanList.MasterScanOrderCount > 0 Then
                            sngComputedScanTime = scanList.MasterScanTimeList(intMasterScanIndex)
                        End If
                    End If

                Case clsCustomSICList.eCustomSICScanTypeConstants.Relative
                    ' sngScanOrAcqTime is a fraction of the total number of scans (for example, 0.5)

                    ' Use the total range of scan times
                    With scanList
                        If .MasterScanOrderCount > 0 Then
                            sngTotalRunTime = .MasterScanTimeList(.MasterScanOrderCount - 1) - .MasterScanTimeList(0)

                            sngComputedScanTime = CSng(sngScanOrAcqTime * sngTotalRunTime + .MasterScanTimeList(0))
                        Else
                            sngComputedScanTime = 0
                        End If
                    End With

                Case clsCustomSICList.eCustomSICScanTypeConstants.AcquisitionTime
                    ' sngScanOrAcqTime is an elution time value (or elution time range)
                    ' No conversion needed; simply return the value
                    sngComputedScanTime = sngScanOrAcqTime

                Case Else
                    ' Unknown type; assume already a scan time
                    sngComputedScanTime = sngScanOrAcqTime
            End Select

        Catch ex As Exception
            ReportError("ScanOrAcqTimeToAbsolute", "Error in clsMasic->ScanOrAcqTimeToScanTime ", ex, True, False)
            sngComputedScanTime = 0
        End Try

        Return sngComputedScanTime

    End Function

End Class