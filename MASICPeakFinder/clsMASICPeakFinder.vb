Option Strict On

' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Copyright 2005, Battelle Memorial Institute.  All Rights Reserved.

' E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
' Website: http://panomics.pnnl.gov/ or http://www.sysbio.org/resources/staff/
' -------------------------------------------------------------------------------
' 
' Licensed under the Apache License, Version 2.0; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at 
' http://www.apache.org/licenses/LICENSE-2.0
'
Public Class clsMASICPeakFinder

#Region "Constants and Enums"
	Public PROGRAM_DATE As String = "November 22, 2013"

    Public Const MINIMUM_PEAK_WIDTH As Integer = 3                         ' Width in points

    Public Enum eNoiseThresholdModes
        AbsoluteThreshold = 0
        TrimmedMeanByAbundance = 1
        TrimmedMeanByCount = 2
        TrimmedMedianByAbundance = 3
        DualTrimmedMeanByAbundance = 4
        MeanOfDataInPeakVicinity = 5
    End Enum

    Public Enum eTTestConfidenceLevelConstants
        Conf80Pct = 0
        Conf90Pct = 1
        Conf95Pct = 2
        Conf98Pct = 3
        Conf99Pct = 4
        Conf99_5Pct = 5
        Conf99_8Pct = 6
        Conf99_9Pct = 7
    End Enum
#End Region

#Region "Structures"

    ' This was previously named udtNoiseThresholdOptionsType
    Public Structure udtBaselineNoiseOptionsType
        Public BaselineNoiseMode As eNoiseThresholdModes        ' Method to use to determine the baseline noise level
        Public BaselineNoiseLevelAbsolute As Single             ' Explicitly defined noise intensity; only used if .BaselineNoiseMode = eNoiseThresholdModes.AbsoluteThreshold; 50000 for SIC, 0 for MS/MS spectra
        Public MinimumSignalToNoiseRatio As Single              ' Typically 2 or 3 for spectra; 0 for SICs
        Public MinimumBaselineNoiseLevel As Single              ' If the noise threshold computed is less than this value, then will use this value to compute S/N; additionally, this is used as the minimum intensity threshold when computing a trimmed noise level
        Public TrimmedMeanFractionLowIntensityDataToAverage As Single   ' Typically 0.75 for SICs, 0.5 for MS/MS spectra; only used for eNoiseThresholdModes.TrimmedMeanByAbundance, .TrimmedMeanByCount, .TrimmedMedianByAbundance
        Public DualTrimmedMeanStdDevLimits As Short                     ' Typically 5; distance from the mean in standard deviation units (SqrRt(Variance)) to discard data for computing the trimmed mean
        Public DualTrimmedMeanMaximumSegments As Short          ' Typically 3; set to 1 to disable segmentation
    End Structure

    Public Structure udtSICPotentialAreaStatsType
        Public MinimumPotentialPeakArea As Double
        Public PeakCountBasisForMinimumPotentialArea As Integer
    End Structure

    Public Structure udtSICPeakFinderOptionsType
        Public IntensityThresholdFractionMax As Single      ' 0.01
        Public IntensityThresholdAbsoluteMinimum As Single  ' 0

        Public SICBaselineNoiseOptions As udtBaselineNoiseOptionsType

        Public MaxDistanceScansNoOverlap As Integer         ' 0         Maximum distance that the edge of an identified peak can be away from the scan number that the parent ion was observed in if the identified peak does not contain the parent ion
        Public MaxAllowedUpwardSpikeFractionMax As Single   ' 0.20      Maximum fraction of the peak maximum that an upward spike can be to be included in the peak
		Public InitialPeakWidthScansScaler As Single		' 0.5       Multiplied by scaled S/N for the given spectrum to determine the initial minimum peak width (in scans) to try.  Scaled "S/N" = Math.Log10(Math.Floor("S/N")) * 10
        Public InitialPeakWidthScansMaximum As Integer      ' 30        Maximum initial peak width to allow

        Public FindPeaksOnSmoothedData As Boolean
        Public SmoothDataRegardlessOfMinimumPeakWidth As Boolean
        Public UseButterworthSmooth As Boolean              ' .UseButterworthSmooth takes precedence over .UseSavitzkyGolaySmooth
        Public ButterworthSamplingFrequency As Single
        Public ButterworthSamplingFrequencyDoubledForSIMData As Boolean

        Public UseSavitzkyGolaySmooth As Boolean
        Public SavitzkyGolayFilterOrder As Short            ' 0         Even number, 0 or greater; 0 means a moving average filter, 2 means a 2nd order Savitzky Golay filter

        Public MassSpectraNoiseThresholdOptions As udtBaselineNoiseOptionsType
    End Structure

    Public Structure udtSmoothedYDataSubsetType
        Public DataCount As Integer
        Public Data() As Single
        Public DataStartIndex As Integer
    End Structure

    Public Structure udtBaselineNoiseStatsType
        Public NoiseLevel As Single             ' Typically the average of the data being sampled to determine the baseline noise estimate
        Public NoiseStDev As Single             ' Standard Deviation of the data used to compute the baseline estimate
        Public PointsUsed As Integer
        Public NoiseThresholdModeUsed As eNoiseThresholdModes
    End Structure

    Public Structure udtBaselineNoiseStatSegmentsType
        Public BaselineNoiseStats As udtBaselineNoiseStatsType
        Public SegmentIndexStart As Integer
        Public SegmentIndexEnd As Integer
    End Structure

    Public Structure udtStatisticalMomentsType
        Public Area As Single                       ' Area; Zeroth central moment (m0); using baseline-corrected intensities unless all of the data is below the baseline -- if that's the case, then using the 3 points surrounding the peak apex
        Public CenterOfMassScan As Integer          ' Center of Mass of the peak; First central moment (m1); reported as an absolute scan number
        Public StDev As Single                      ' Standard Deviation; Sqrt(Variance) where Variance is the second central moment (m2)
        Public Skew As Single                       ' Computed using the third central moment via m3 / sigma^3 where m3 is the third central moment and sigma^3 = (Sqrt(m2))^3
        Public KSStat As Single                     ' The Kolmogorov-Smirnov Goodness-of-Fit value (not officially a statistical moment, but we'll put it here anyway)
        Public DataCountUsed As Integer
    End Structure

    Public Structure udtSICStatsPeakType
        Public IndexBaseLeft As Integer         ' Index that the SIC peak officially starts; Pointer to entry in .SICData()
        Public IndexBaseRight As Integer        ' Index that the SIC peak officially ends; Pointer to entry in .SICData() 
        Public IndexMax As Integer              ' Index of the maximum of the SIC peak; Pointer to entry in .SICData() 
        Public IndexObserved As Integer         ' Index that the SIC peak was first observed in by the instrument(and thus caused it to be chosen for fragmentation); Pointer to entry in .SICData()
        Public ParentIonIntensity As Single     ' Intensity of the parent ion in the scan just prior to the scan in which the peptide was fragmented; if previous scan was not MS1, then interpolates between MS1 scans bracketing the MS2 scan

        Public PreviousPeakFWHMPointRight As Integer    ' Index of the FWHM point in the previous closest peak in the SIC; filtering to only include peaks with intensities >= BestPeak'sIntensity/3
        Public NextPeakFWHMPointLeft As Integer         ' Index of the FWHM point in the next closest peak in the SIC; filtering to only include peaks with intensities >= BestPeak'sIntensity/3

        Public FWHMScanWidth As Integer

        Public MaxIntensityValue As Single      ' Maximum intensity of the SIC Peak -- not necessarily the maximum intensity in .SICData(); Not baseline corrected
        Public Area As Single                   ' Area of the SIC peak -- Equivalent to the zeroth statistical moment (m0); Not baseline corrected
        Public ShoulderCount As Integer         ' Number of small peaks that are contained by the peak
        Public SignalToNoiseRatio As Single

        Public BaselineNoiseStats As udtBaselineNoiseStatsType
        Public StatisticalMoments As udtStatisticalMomentsType

    End Structure

    Private Structure udtFindPeaksDataType

        Public OriginalPeakLocationIndex As Integer

        Public SourceDataCount As Integer
        Public XData() As Double
        Public YData() As Double
        Public SmoothedYData() As Double

        Public PeakCount As Integer
        Public PeakLocs() As Integer
        Public PeakEdgesLeft() As Integer
        Public PeakEdgesRight() As Integer
        Public PeakAreas() As Double
        Public PeakIsValid() As Boolean

        Public PeakWidthPointsMinimum As Integer
        Public MaxAllowedUpwardSpikeFractionMax As Single
        Public BestPeakIndex As Integer
        Public BestPeakArea As Single

    End Structure
#End Region

#Region "Classwide Variables"
    Private mShowMessages As Boolean
    Private mStatusMessage As String
    Private mErrorLogger As PRISM.Logging.ILogger
#End Region

#Region "Properties"
    Public ReadOnly Property ProgramDate() As String
        Get
            Return PROGRAM_DATE
        End Get
    End Property

    Public ReadOnly Property ProgramVersion() As String
        Get
            Return GetVersionForExecutingAssembly()
        End Get
    End Property

    Public Property ShowMessages() As Boolean
        Get
            Return mShowMessages
        End Get
        Set(ByVal Value As Boolean)
            mShowMessages = Value
        End Set
    End Property

    Public ReadOnly Property StatusMessage() As String
        Get
            Return mStatusMessage
        End Get
    End Property
#End Region

    Public Sub AttachErrorLogger(ByVal objLogger As PRISM.Logging.ILogger, ByVal intDebugLevel As Integer)
        If Not mErrorLogger Is Nothing Then Exit Sub
        mErrorLogger = objLogger
    End Sub

    Public Shared Function BaselineAdjustArea(ByRef udtSICPeak As udtSICStatsPeakType, ByVal intSICPeakWidthFullScans As Integer, ByVal blnAllowNegativeValues As Boolean) As Single
        ' Note, compute intSICPeakWidthFullScans using:
        '  Width = SICScanNumbers(.Peak.IndexBaseRight) - SICScanNumbers(.Peak.IndexBaseLeft) + 1

        With udtSICPeak
            Return BaselineAdjustArea(.Area, .BaselineNoiseStats.NoiseLevel, .FWHMScanWidth, intSICPeakWidthFullScans, blnAllowNegativeValues)
        End With
    End Function

    Public Shared Function BaselineAdjustArea(ByVal sngPeakArea As Single, ByVal sngBaselineNoiseLevel As Single, ByVal intSICPeakFWHMScans As Integer, ByVal intSICPeakWidthFullScans As Integer, ByVal blnAllowNegativeValues As Boolean) As Single
        Dim sngCorrectedArea As Single
        Dim intWidthToSubtract As Integer

        intWidthToSubtract = ComputeWidthAtBaseUsingFWHM(intSICPeakFWHMScans, intSICPeakWidthFullScans, 4)

        sngCorrectedArea = sngPeakArea - sngBaselineNoiseLevel * intWidthToSubtract
        If blnAllowNegativeValues OrElse sngCorrectedArea > 0 Then
            Return sngCorrectedArea
        Else
            Return 0
        End If
    End Function

    Public Shared Function BaselineAdjustIntensity(ByRef udtSICPeak As udtSICStatsPeakType, ByVal blnAllowNegativeValues As Boolean) As Single
        With udtSICPeak
            Return BaselineAdjustIntensity(.MaxIntensityValue, .BaselineNoiseStats.NoiseLevel, blnAllowNegativeValues)
        End With
    End Function

    Public Shared Function BaselineAdjustIntensity(ByVal sngRawIntensity As Single, ByVal sngBaselineNoiseLevel As Single, ByVal blnAllowNegativeValues As Boolean) As Single
        If blnAllowNegativeValues OrElse sngRawIntensity > sngBaselineNoiseLevel Then
            Return sngRawIntensity - sngBaselineNoiseLevel
        Else
            Return 0
        End If
    End Function

    Private Function ComputeAverageNoiseLevelCheckCounts(ByVal intValidDataCountA As Integer, ByVal intValidDataCountB As Integer, ByVal dblSumA As Double, ByVal dblSumB As Double, ByVal intMinimumCount As Integer, ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType) As Boolean

        Dim blnUseBothSides As Boolean
        Dim blnUseLeftData As Boolean
        Dim blnUseRightData As Boolean

        If intMinimumCount < 1 Then intMinimumCount = 1
        blnUseBothSides = False

        If intValidDataCountA >= intMinimumCount OrElse intValidDataCountB >= intMinimumCount Then

            If intValidDataCountA >= intMinimumCount AndAlso intValidDataCountB >= intMinimumCount Then
                ' Both meet the minimum count criterion
                ' Return an overall average
                blnUseBothSides = True
            ElseIf intValidDataCountA >= intMinimumCount Then
                blnUseLeftData = True
            Else
                blnUseRightData = True
            End If

            If blnUseBothSides Then
                With udtBaselineNoiseStats
                    .NoiseLevel = CSng((dblSumA + dblSumB) / (intValidDataCountA + intValidDataCountB))
                    .NoiseStDev = 0      ' We'll compute noise StDev outside this function
                    .PointsUsed = intValidDataCountA + intValidDataCountB
                End With
            Else
                If blnUseLeftData Then
                    ' Use left data only
                    With udtBaselineNoiseStats
                        .NoiseLevel = CSng(dblSumA / intValidDataCountA)
                        .NoiseStDev = 0
                        .PointsUsed = intValidDataCountA
                    End With
				ElseIf blnUseRightData Then
					' Use right data only
					With udtBaselineNoiseStats
						.NoiseLevel = CSng(dblSumB / intValidDataCountB)
						.NoiseStDev = 0
						.PointsUsed = intValidDataCountB
					End With
				Else
					Throw New Exception("Logic error; This code should not be reached")
				End If
            End If
            Return True
        Else
            Return False
        End If

    End Function

	Private Function ComputeAverageNoiseLevelExcludingRegion(ByVal intDatacount As Integer, ByVal sngData() As Single, ByVal intIndexStart As Integer, ByVal intIndexEnd As Integer, ByVal intExclusionIndexStart As Integer, ByVal intExclusionIndexEnd As Integer, ByRef udtBaselineNoiseOptions As udtBaselineNoiseOptionsType, ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType) As Boolean

		' Compute the average intensity level between intIndexStart and intExclusionIndexStart
		' Also compute the average between intExclusionIndexEnd and intIndexEnd
		' Use ComputeAverageNoiseLevelCheckCounts to determine whether both averages are used to determine
		'  the baseline noise level or whether just one of the averages is used

		Dim intIndex As Integer
		Dim intValidDataCountA As Integer
		Dim intValidDataCountB As Integer

		Dim dblSumA As Double
		Dim dblSumB As Double
		Dim sngMinimumPositiveValue As Single

		Dim blnSuccess As Boolean

		blnSuccess = False

		' Examine the exclusion range.  If the exclusion range excludes all
		'  data to the left or right of the peak, then use a few data points anyway, even if this does include some of the peak
		If intExclusionIndexStart < intIndexStart + MINIMUM_PEAK_WIDTH Then
			intExclusionIndexStart = intIndexStart + MINIMUM_PEAK_WIDTH
			If intExclusionIndexStart >= intIndexEnd Then
				intExclusionIndexStart = intIndexEnd - 1
			End If
		End If

		If intExclusionIndexEnd > intIndexEnd - MINIMUM_PEAK_WIDTH Then
			intExclusionIndexEnd = intIndexEnd - MINIMUM_PEAK_WIDTH
			If intExclusionIndexEnd < 0 Then
				intExclusionIndexEnd = 0
			End If
		End If

		If intExclusionIndexStart >= intIndexStart AndAlso intExclusionIndexStart <= intIndexEnd AndAlso _
		   intExclusionIndexEnd >= intExclusionIndexStart AndAlso intExclusionIndexEnd <= intIndexEnd _
		   Then

			sngMinimumPositiveValue = FindMinimumPositiveValue(intDatacount, sngData, 1)

			intValidDataCountA = 0
			dblSumA = 0
			For intIndex = intIndexStart To intExclusionIndexStart
				dblSumA += Math.Max(sngMinimumPositiveValue, sngData(intIndex))
				intValidDataCountA += 1
			Next intIndex

			intValidDataCountB = 0
			dblSumB = 0
			For intIndex = intExclusionIndexEnd To intIndexEnd
				dblSumB += Math.Max(sngMinimumPositiveValue, sngData(intIndex))
				intValidDataCountB += 1
			Next intIndex

			blnSuccess = ComputeAverageNoiseLevelCheckCounts(intValidDataCountA, intValidDataCountB, dblSumA, dblSumB, MINIMUM_PEAK_WIDTH, udtBaselineNoiseStats)

			' Assure that .NoiseLevel is at least as large as sngMinimumPositiveValue
			If udtBaselineNoiseStats.NoiseLevel < sngMinimumPositiveValue Then
				udtBaselineNoiseStats.NoiseLevel = sngMinimumPositiveValue
			End If

			' Populate .NoiseStDev
			With udtBaselineNoiseStats
				intValidDataCountA = 0
				intValidDataCountB = 0
				dblSumA = 0
				dblSumB = 0
				If .PointsUsed > 0 Then
					For intIndex = intIndexStart To intExclusionIndexStart
						dblSumA += (Math.Max(sngMinimumPositiveValue, sngData(intIndex)) - .NoiseLevel) ^ 2
						intValidDataCountA += 1
					Next intIndex

					For intIndex = intExclusionIndexEnd To intIndexEnd
						dblSumB += (Math.Max(sngMinimumPositiveValue, sngData(intIndex)) - .NoiseLevel) ^ 2
						intValidDataCountB += 1
					Next intIndex
				End If

				If intValidDataCountA + intValidDataCountB > 0 Then
					.NoiseStDev = CSng(Math.Sqrt((dblSumA + dblSumB) / (intValidDataCountA + intValidDataCountB)))
				Else
					.NoiseStDev = 0
				End If
			End With

		End If

		If Not blnSuccess Then
			Dim udtBaselineNoiseOptionsOverride As udtBaselineNoiseOptionsType

			udtBaselineNoiseOptionsOverride = udtBaselineNoiseOptions
			With udtBaselineNoiseOptionsOverride
				.BaselineNoiseMode = eNoiseThresholdModes.TrimmedMedianByAbundance
				.TrimmedMeanFractionLowIntensityDataToAverage = 0.33
			End With

			blnSuccess = ComputeTrimmedNoiseLevel(sngData, intIndexStart, intIndexEnd, udtBaselineNoiseOptionsOverride, False, udtBaselineNoiseStats)
		End If

		Return blnSuccess

	End Function

	Public Function ComputeDualTrimmedNoiseLevelTTest(ByVal sngData() As Single, ByVal intIndexStart As Integer, ByVal intIndexEnd As Integer, ByVal udtBaselineNoiseOptions As udtBaselineNoiseOptionsType, ByRef udtBaselineNoiseStats() As udtBaselineNoiseStatSegmentsType) As Boolean

		' Divide the data into the number of segments given by udtBaselineNoiseOptions.DualTrimmedMeanMaximumSegments  (use 3 by default)
		' Call ComputeDualTrimmedNoiseLevel for each segment
		' Use a TTest to determine whether we need to define a custom noise threshold for each segment

		Dim udtPrevSegmentStats As udtBaselineNoiseStatsType
		Dim eConfidenceLevel As eTTestConfidenceLevelConstants

		Dim intSegmentCountLocal As Integer
		Dim intSegmentLength As Integer
		Dim intSegmentIndex As Integer
		Dim intSegmentIndexCopy As Integer

		Dim dblTCalculated As Double
		Dim blnSignificantDifference As Boolean

		Try

			intSegmentCountLocal = udtBaselineNoiseOptions.DualTrimmedMeanMaximumSegments
			If intSegmentCountLocal = 0 Then intSegmentCountLocal = 3
			If intSegmentCountLocal < 1 Then intSegmentCountLocal = 1

			ReDim udtBaselineNoiseStats(intSegmentCountLocal - 1)

			' Initialize BaselineNoiseStats for each segment now, in case an error occurs
			For intSegmentIndex = 0 To intSegmentCountLocal - 1
				InitializeBaselineNoiseStats(udtBaselineNoiseStats(intSegmentIndex).BaselineNoiseStats, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel, eNoiseThresholdModes.DualTrimmedMeanByAbundance)
			Next intSegmentIndex

			' Determine the segment length
			intSegmentLength = CInt(Math.Round((intIndexEnd - intIndexStart) / intSegmentCountLocal, 0))

			' Initialize the first segment
			udtBaselineNoiseStats(0).SegmentIndexStart = intIndexStart
			If intSegmentCountLocal = 1 Then
				udtBaselineNoiseStats(0).SegmentIndexEnd = intIndexEnd
			Else
				udtBaselineNoiseStats(0).SegmentIndexEnd = udtBaselineNoiseStats(0).SegmentIndexStart + intSegmentLength - 1
			End If

			' Initialize the remaining segments
			For intSegmentIndex = 1 To intSegmentCountLocal - 1
				udtBaselineNoiseStats(intSegmentIndex).SegmentIndexStart = udtBaselineNoiseStats(intSegmentIndex - 1).SegmentIndexEnd + 1
				If intSegmentIndex = intSegmentCountLocal - 1 Then
					udtBaselineNoiseStats(intSegmentIndex).SegmentIndexEnd = intIndexEnd
				Else
					udtBaselineNoiseStats(intSegmentIndex).SegmentIndexEnd = udtBaselineNoiseStats(intSegmentIndex).SegmentIndexStart + intSegmentLength - 1
				End If
			Next intSegmentIndex

			' Call ComputeDualTrimmedNoiseLevel for each segment
			For intSegmentIndex = 0 To intSegmentCountLocal - 1
				With udtBaselineNoiseStats(intSegmentIndex)
					ComputeDualTrimmedNoiseLevel(sngData, .SegmentIndexStart, .SegmentIndexEnd, udtBaselineNoiseOptions, .BaselineNoiseStats)
				End With
			Next intSegmentIndex

			' Compare adjacent segments using a T-Test, starting with the final segment and working backward
			eConfidenceLevel = eTTestConfidenceLevelConstants.Conf90Pct
			intSegmentIndex = intSegmentCountLocal - 1
			Do While intSegmentIndex > 0
				udtPrevSegmentStats = udtBaselineNoiseStats(intSegmentIndex - 1).BaselineNoiseStats
				With udtBaselineNoiseStats(intSegmentIndex).BaselineNoiseStats
					blnSignificantDifference = TestSignificanceUsingTTest(.NoiseLevel, udtPrevSegmentStats.NoiseLevel, .NoiseStDev, udtPrevSegmentStats.NoiseStDev, .PointsUsed, udtPrevSegmentStats.PointsUsed, eConfidenceLevel, dblTCalculated)
				End With

				If blnSignificantDifference Then
					' Significant difference; leave the 2 segments intact
				Else
					' Not a significant difference; recompute the Baseline Noise stats using the two segments combined
					With udtBaselineNoiseStats(intSegmentIndex - 1)
						.SegmentIndexEnd = udtBaselineNoiseStats(intSegmentIndex).SegmentIndexEnd
						ComputeDualTrimmedNoiseLevel(sngData, .SegmentIndexStart, .SegmentIndexEnd, udtBaselineNoiseOptions, .BaselineNoiseStats)
					End With

					For intSegmentIndexCopy = intSegmentIndex To intSegmentCountLocal - 2
						udtBaselineNoiseStats(intSegmentIndexCopy) = udtBaselineNoiseStats(intSegmentIndexCopy + 1)
					Next intSegmentIndexCopy
					intSegmentCountLocal -= 1
				End If
				intSegmentIndex -= 1
			Loop

			If intSegmentCountLocal <> udtBaselineNoiseStats.Length Then
				ReDim Preserve udtBaselineNoiseStats(intSegmentCountLocal - 1)
			End If
		Catch ex As Exception
			Return False
		End Try

		Return True

	End Function

	Public Function ComputeDualTrimmedNoiseLevel(ByVal sngData() As Single, ByVal intIndexStart As Integer, ByVal intIndexEnd As Integer, ByVal udtBaselineNoiseOptions As udtBaselineNoiseOptionsType, ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType) As Boolean
		' Computes the average of all of the data in sngData()
		' Next, discards the data above and below udtBaselineNoiseOptions.DualTrimmedMeanStdDevLimits of the mean
		' Finally, recomputes the average using the data that remains
		' Returns True if success, False if error (or no data in sngData)

		' Note: Replaces values of 0 with the minimum positive value in sngData()
		' Note: You cannot use sngData.Length to determine the length of the array; use intIndexStart and intIndexEnd to find the limits

		Dim dblIntensityThresholdMin As Double
		Dim dblIntensityThresholdMax As Double

		Dim dblSum As Double
		Dim dblAverage As Double
		Dim dblVariance As Double

		Dim intIndex As Integer

		Dim intDataSortedCount As Integer
		Dim sngDataSorted() As Single
		Dim sngMinimumPositiveValue As Single

		Dim intDataSortedIndexStart As Integer
		Dim intDataSortedIndexEnd As Integer
		Dim intDataUsedCount As Integer

		' Initialize udtBaselineNoiseStats
		InitializeBaselineNoiseStats(udtBaselineNoiseStats, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel, eNoiseThresholdModes.DualTrimmedMeanByAbundance)

		If sngData Is Nothing OrElse intIndexEnd - intIndexStart < 0 Then
			Return False
		End If

		' Copy the data into sngDataSorted
		intDataSortedCount = intIndexEnd - intIndexStart + 1
		ReDim sngDataSorted(intDataSortedCount - 1)

		For intIndex = intIndexStart To intIndexEnd
			sngDataSorted(intIndex - intIndexStart) = sngData(intIndex)
		Next intIndex

		' Sort the array
		Array.Sort(sngDataSorted)

		' Look for the minimum positive value and replace all data in sngDataSorted with that value
		sngMinimumPositiveValue = ReplaceSortedDataWithMinimumPositiveValue(intDataSortedCount, sngDataSorted)

		' Initialize the indices to use in sngDataSorted()
		intDataSortedIndexStart = 0
		intDataSortedIndexEnd = intDataSortedCount - 1

		' Compute the average using the data in sngDataSorted between intDataSortedIndexStart and intDataSortedIndexEnd (i.e. all the data)
		dblSum = 0
		For intIndex = intDataSortedIndexStart To intDataSortedIndexEnd
			dblSum += sngDataSorted(intIndex)
		Next intIndex
		intDataUsedCount = intDataSortedIndexEnd - intDataSortedIndexStart + 1
		dblAverage = dblSum / intDataUsedCount

		If intDataUsedCount > 1 Then
			' Compute the variance (this is a sample variance, not a population variance)
			dblSum = 0
			For intIndex = intDataSortedIndexStart To intDataSortedIndexEnd
				dblSum += (sngDataSorted(intIndex) - dblAverage) ^ 2
			Next intIndex
			dblVariance = dblSum / (intDataUsedCount - 1)
		Else
			dblVariance = 0
		End If

		If udtBaselineNoiseOptions.DualTrimmedMeanStdDevLimits < 1 Then
			udtBaselineNoiseOptions.DualTrimmedMeanStdDevLimits = 1
		End If

		' Note: Standard Deviation = sigma = SquareRoot(Variance)
		dblIntensityThresholdMin = dblAverage - Math.Sqrt(dblVariance) * udtBaselineNoiseOptions.DualTrimmedMeanStdDevLimits
		dblIntensityThresholdMax = dblAverage + Math.Sqrt(dblVariance) * udtBaselineNoiseOptions.DualTrimmedMeanStdDevLimits

		' Recompute the average using only the data between dblIntensityThresholdMin and dblIntensityThresholdMax in sngDataSorted
		dblSum = 0
		intIndex = intDataSortedIndexStart
		Do While intIndex <= intDataSortedIndexEnd
			If sngDataSorted(intIndex) >= dblIntensityThresholdMin Then
				intDataSortedIndexStart = intIndex
				Do While intIndex <= intDataSortedIndexEnd
					If sngDataSorted(intIndex) <= dblIntensityThresholdMax Then
						dblSum += sngDataSorted(intIndex)
					Else
						intDataSortedIndexEnd = intIndex - 1
						Exit Do
					End If
					intIndex += 1
				Loop
			End If
			intIndex += 1
		Loop
		intDataUsedCount = intDataSortedIndexEnd - intDataSortedIndexStart + 1

		If intDataUsedCount > 0 Then
			udtBaselineNoiseStats.NoiseLevel = CSng(dblSum / intDataUsedCount)

			' Compute the variance (this is a sample variance, not a population variance)
			dblSum = 0
			For intIndex = intDataSortedIndexStart To intDataSortedIndexEnd
				dblSum += (sngDataSorted(intIndex) - udtBaselineNoiseStats.NoiseLevel) ^ 2
			Next intIndex

			With udtBaselineNoiseStats
				If intDataUsedCount > 1 Then
					.NoiseStDev = CSng(Math.Sqrt(dblSum / (intDataUsedCount - 1)))
				Else
					.NoiseStDev = 0
				End If
				.PointsUsed = intDataUsedCount
			End With

		Else
			udtBaselineNoiseStats.NoiseLevel = Math.Max(sngMinimumPositiveValue, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel)
			udtBaselineNoiseStats.NoiseStDev = 0
		End If

		' Assure that .NoiseLevel is >= .MinimumBaselineNoiseLevel
		With udtBaselineNoiseStats
			If .NoiseLevel < udtBaselineNoiseOptions.MinimumBaselineNoiseLevel AndAlso udtBaselineNoiseOptions.MinimumBaselineNoiseLevel > 0 Then
				.NoiseLevel = udtBaselineNoiseOptions.MinimumBaselineNoiseLevel
				.NoiseStDev = 0								' Set this to 0 since we have overridden .NoiseLevel
			End If
		End With

		Return True

	End Function

	Private Function ComputeFWHM(ByVal SICScanNumbers() As Integer, ByVal SICData() As Single, ByRef udtSICPeak As udtSICStatsPeakType, ByVal blnSubtractBaselineNoise As Boolean) As Integer
		' Note: The calling function should have already populated udtSICPeak.MaxIntensityValue, plus .IndexMax, .IndexBaseLeft, and .IndexBaseRight
		' If blnSubtractBaselineNoise is True, then this function also uses udtSICPeak.BaselineNoiseStats....
		' Note: This function returns the FWHM value in units of scan number; it does not update the value stored in udtSICPeak
		' This function does, however, update udtSICPeak.IndexMax if it is not between udtSICPeak.IndexBaseLeft and udtSICPeak.IndexBaseRight

		Const ALLOW_NEGATIVE_VALUES As Boolean = False
		Dim sngFWHMScanStart, sngFWHMScanEnd As Single
		Dim intFWHMScans As Integer

		Dim intDataIndex As Integer
		Dim sngTargetIntensity As Single
		Dim sngMaximumIntensity As Single

		Dim sngY1, sngY2 As Single

		' Determine the full width at half max (FWHM), in units of absolute scan number
		Try
			With udtSICPeak

				If .IndexMax <= .IndexBaseLeft OrElse .IndexMax >= .IndexBaseRight Then
					' Find the index of the maximum (between .IndexBaseLeft and .IndexBaseRight)
					sngMaximumIntensity = 0
					If .IndexMax < .IndexBaseLeft OrElse .IndexMax > .IndexBaseRight Then
						.IndexMax = .IndexBaseLeft
					End If

					For intDataIndex = .IndexBaseLeft To .IndexBaseRight
						If SICData(intDataIndex) > sngMaximumIntensity Then
							.IndexMax = intDataIndex
							sngMaximumIntensity = SICData(intDataIndex)
						End If
					Next intDataIndex
				End If

				' Look for the intensity halfway down the peak (correcting for baseline noise level if blnSubtractBaselineNoise = True)
				If blnSubtractBaselineNoise Then
					sngTargetIntensity = BaselineAdjustIntensity(.MaxIntensityValue, .BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES) / 2

					If sngTargetIntensity <= 0 Then
						' The maximum intensity of the peak is below the baseline; do not correct for baseline noise level
						sngTargetIntensity = .MaxIntensityValue / 2
						blnSubtractBaselineNoise = False
					End If
				Else
					sngTargetIntensity = .MaxIntensityValue / 2
				End If

				If sngTargetIntensity > 0 Then

					' Start the search at each peak edge to thus determine the largest FWHM value
					sngFWHMScanStart = -1
					For intDataIndex = .IndexBaseLeft To .IndexMax - 1
						If blnSubtractBaselineNoise Then
							sngY1 = BaselineAdjustIntensity(SICData(intDataIndex), .BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
							sngY2 = BaselineAdjustIntensity(SICData(intDataIndex + 1), .BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
						Else
							sngY1 = SICData(intDataIndex)
							sngY2 = SICData(intDataIndex + 1)
						End If

						If sngY1 > sngTargetIntensity OrElse sngY2 > sngTargetIntensity Then
							If sngY1 <= sngTargetIntensity AndAlso sngY2 >= sngTargetIntensity Then
								InterpolateX(sngFWHMScanStart, SICScanNumbers(intDataIndex), SICScanNumbers(intDataIndex + 1), sngY1, sngY2, sngTargetIntensity)
							Else
								' sngTargetIntensity is not between sngY1 and sngY2; simply use intDataIndex
								If intDataIndex = .IndexBaseLeft Then
									' At the start of the peak; use the scan number halfway between .IndexBaseLeft and .IndexMax
									sngFWHMScanStart = SICScanNumbers(intDataIndex + CInt(Math.Round((.IndexMax - .IndexBaseLeft) / 2, 0)))
								Else
									' This code will probably never be reached
									sngFWHMScanStart = SICScanNumbers(intDataIndex)
								End If
							End If
							Exit For
						End If
					Next intDataIndex
					If sngFWHMScanStart < 0 Then
						If .IndexMax > .IndexBaseLeft Then
							sngFWHMScanStart = SICScanNumbers(.IndexMax - 1)
						Else
							sngFWHMScanStart = SICScanNumbers(.IndexBaseLeft)
						End If
					End If

					sngFWHMScanEnd = -1
					For intDataIndex = .IndexBaseRight - 1 To .IndexMax Step -1
						If blnSubtractBaselineNoise Then
							sngY1 = BaselineAdjustIntensity(SICData(intDataIndex), .BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
							sngY2 = BaselineAdjustIntensity(SICData(intDataIndex + 1), .BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
						Else
							sngY1 = SICData(intDataIndex)
							sngY2 = SICData(intDataIndex + 1)
						End If

						If sngY1 > sngTargetIntensity OrElse sngY2 > sngTargetIntensity Then
							If sngY1 >= sngTargetIntensity AndAlso sngY2 <= sngTargetIntensity Then
								InterpolateX(sngFWHMScanEnd, SICScanNumbers(intDataIndex), SICScanNumbers(intDataIndex + 1), sngY1, sngY2, sngTargetIntensity)
							Else
								' sngTargetIntensity is not between sngY1 and sngY2; simply use intDataIndex+1
								If intDataIndex = .IndexBaseRight - 1 Then
									' At the end of the peak; use the scan number halfway between .IndexBaseRight and .IndexMax
									sngFWHMScanEnd = SICScanNumbers(intDataIndex + 1 - CInt(Math.Round((.IndexBaseRight - .IndexMax) / 2, 0)))
								Else
									' This code will probably never be reached
									sngFWHMScanEnd = SICScanNumbers(intDataIndex + 1)
								End If
							End If
							Exit For
						End If
					Next intDataIndex
					If sngFWHMScanEnd < 0 Then
						If .IndexMax < .IndexBaseRight Then
							sngFWHMScanEnd = SICScanNumbers(.IndexMax + 1)
						Else
							sngFWHMScanEnd = SICScanNumbers(.IndexBaseRight)
						End If
					End If

					intFWHMScans = CInt(Math.Round(sngFWHMScanEnd - sngFWHMScanStart, 0))
					If intFWHMScans <= 0 Then intFWHMScans = 0
				Else
					' Maximum intensity value is <= 0
					' Set FWHM to 1
					intFWHMScans = 1
				End If

			End With

		Catch ex As Exception
			LogErrors("clsMASICPeakFinder->ComputeFWHM", "Error finding FWHM", ex, True, False, True)
			intFWHMScans = 0
		End Try

		Return intFWHMScans

	End Function

	Public Sub TestComputeKSStat()
		Dim ScanAtApex As Integer
		Dim FWHM As Single

		Dim intScanNumbers() As Integer = New Integer() {0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40}
		Dim sngIntensities() As Single = New Single() {2, 5, 7, 10, 11, 18, 19, 15, 8, 4, 1}

		ScanAtApex = 20
		FWHM = 25

		Dim peakMean As Single
		Dim peakStDev As Double

		peakMean = ScanAtApex
		' FWHM / 2.35482 = FWHM / (2 * Sqrt(2 * Ln(2)))
		peakStDev = FWHM / 2.35482
		'peakStDev = 28.8312

		ComputeKSStatistic(intScanNumbers.Length, intScanNumbers, sngIntensities, peakMean, peakStDev)

		' ToDo: Update program to call ComputeKSStatistic

		' ToDo: Update Statistical Moments computation to:
		'  a) Create baseline adjusted intensity values
		'  b) Remove the contiguous data from either end that is <= 0
		'  c) Step through the remaining data and interpolate across gaps with intensities of 0 (linear interpolation)
		'  d) Use this final data to compute the statistical moments and KS Statistic

		' If less than 3 points remain with the above procedure, then use the 5 points centered around the peak maximum, non-baseline corrected data

	End Sub

	Private Function ComputeKSStatistic(ByVal intDataCount As Integer, ByVal intXDataIn() As Integer, ByVal sngYDataIn() As Single, ByVal peakMean As Single, ByVal peakStDev As Double) As Double
		Dim intScanOffset As Integer
		Dim intXData() As Integer
		Dim dblYData() As Double

		Dim dblYDataNormalized() As Double
		Dim dblXDataPDF() As Double
		Dim dblYDataEDF() As Double
		Dim dblXDataCDF() As Double

		Dim KS_gof As Double
		Dim dblCompare As Double

		Dim dblYDataSum As Double
		Dim intIndex As Integer

		' Copy data from intXDataIn() to intXData, subtracting the value in intXDataIn(0) from each scan
		ReDim intXData(intDataCount - 1)
		ReDim dblYData(intDataCount - 1)

		intScanOffset = intXDataIn(0)
		For intIndex = 0 To intDataCount - 1
			intXData(intIndex) = intXDataIn(intIndex) - intScanOffset
			dblYData(intIndex) = sngYDataIn(intIndex)
		Next intIndex

		dblYDataSum = 0
		For intIndex = 0 To dblYData.Length - 1
			dblYDataSum += dblYData(intIndex)
		Next intIndex
		If Math.Abs(dblYDataSum - 0) < Double.Epsilon Then dblYDataSum = 1

		' Compute the Vector of normalized intensities = observed pdf
		ReDim dblYDataNormalized(dblYData.Length - 1)
		For intIndex = 0 To dblYData.Length - 1
			dblYDataNormalized(intIndex) = dblYData(intIndex) / dblYDataSum
		Next intIndex

		' Estimate the empirical distribution function (EDF) using an accumulating sum
		dblYDataSum = 0
		ReDim dblYDataEDF(dblYDataNormalized.Length - 1)
		For intIndex = 0 To dblYDataNormalized.Length - 1
			dblYDataSum += dblYDataNormalized(intIndex)
			dblYDataEDF(intIndex) = dblYDataSum
		Next intIndex

		' Compute the Vector of Normal PDF values evaluated at the X values in the peak window
		ReDim dblXDataPDF(intXData.Length - 1)
		For intIndex = 0 To intXData.Length - 1
			dblXDataPDF(intIndex) = (1 / (Math.Sqrt(2 * Math.PI) * peakStDev)) * Math.Exp((-1 / 2) * ((intXData(intIndex) - (peakMean - intScanOffset)) / peakStDev) ^ 2)
		Next intIndex

		Dim dblXDataPDFSum As Double
		dblXDataPDFSum = 0
		For intIndex = 0 To dblXDataPDF.Length - 1
			dblXDataPDFSum += dblXDataPDF(intIndex)
		Next intIndex

		' Estimate the theoretical CDF using an accumulating sum
		ReDim dblXDataCDF(dblXDataPDF.Length - 1)
		dblYDataSum = 0
		For intIndex = 0 To dblXDataPDF.Length - 1
			dblYDataSum += dblXDataPDF(intIndex)
			dblXDataCDF(intIndex) = dblYDataSum / ((1 + (1 / intXData.Length)) * dblXDataPDFSum)
		Next intIndex

		' Compute the maximum of the absolute differences between the YData EDF and XData CDF
		KS_gof = 0
		For intIndex = 0 To dblXDataCDF.Length - 1
			dblCompare = Math.Abs(dblYDataEDF(intIndex) - dblXDataCDF(intIndex))
			If dblCompare > KS_gof Then
				KS_gof = dblCompare
			End If
		Next intIndex

		Return Math.Sqrt(intXData.Length) * KS_gof	 '  return modified KS statistic

	End Function

	Public Function ComputeNoiseLevelForSICData(ByVal intDatacount As Integer, ByVal sngData() As Single, ByVal udtBaselineNoiseOptions As udtBaselineNoiseOptionsType, ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType) As Boolean
		' Updates udtBaselineNoiseStats with the baseline noise level
		' Returns True if success, false in an error

		Const IGNORE_NON_POSITIVE_DATA As Boolean = False

		If udtBaselineNoiseOptions.BaselineNoiseMode = eNoiseThresholdModes.AbsoluteThreshold Then
			udtBaselineNoiseStats.NoiseLevel = udtBaselineNoiseOptions.BaselineNoiseLevelAbsolute
			Return True
		ElseIf udtBaselineNoiseOptions.BaselineNoiseMode = eNoiseThresholdModes.DualTrimmedMeanByAbundance Then
			Return ComputeDualTrimmedNoiseLevel(sngData, 0, intDatacount - 1, udtBaselineNoiseOptions, udtBaselineNoiseStats)
		Else
			Return ComputeTrimmedNoiseLevel(sngData, 0, intDatacount - 1, udtBaselineNoiseOptions, IGNORE_NON_POSITIVE_DATA, udtBaselineNoiseStats)
		End If
	End Function

	Public Function ComputeNoiseLevelInPeakVicinity(ByVal intDatacount As Integer, ByVal SICScanNumbers() As Integer, ByVal sngData() As Single, ByRef udtSICPeak As udtSICStatsPeakType, ByVal udtBaselineNoiseOptions As udtBaselineNoiseOptionsType) As Boolean

		Const NOISE_ESTIMATE_DATACOUNT_MINIMUM As Integer = 5
		Const NOISE_ESTIMATE_DATACOUNT_MAXIMUM As Integer = 100

		Dim intIndexStart As Integer
		Dim intIndexEnd As Integer

		Dim intIndexBaseLeft As Integer
		Dim intIndexBaseRight As Integer

		Dim intPeakWidthPoints As Integer
		Dim intPeakHalfWidthPoints As Integer

		Dim intPeakWidthBaseScans As Integer		' Minimum of peak width at 4 sigma vs. intPeakWidthFullScans

		Dim blnSuccess As Boolean
		Dim blnShiftLeft As Boolean

		' Initialize udtBaselineNoiseStats
		InitializeBaselineNoiseStats(udtSICPeak.BaselineNoiseStats, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel, eNoiseThresholdModes.MeanOfDataInPeakVicinity)

		' Only use a portion of the data to compute the noise level
		' The number of points to extend from the left and right is based on the width at 4 sigma; useful for tailing peaks
		' Also, determine the peak start using the smaller of the width at 4 sigma vs. the observed peak width

		' Estimate FWHM since it is sometimes not yet known when this function is called
		' The reason it's not yet know is that the final FWHM value is computed using baseline corrected intensity data, but
		'  the whole purpose of this function is to compute the baseline level
		udtSICPeak.FWHMScanWidth = ComputeFWHM(SICScanNumbers, sngData, udtSICPeak, False)
		intPeakWidthBaseScans = ComputeWidthAtBaseUsingFWHM(udtSICPeak, SICScanNumbers, 4)
		intPeakWidthPoints = ConvertScanWidthToPoints(intPeakWidthBaseScans, udtSICPeak, SICScanNumbers)

		intPeakHalfWidthPoints = CInt(Math.Round(intPeakWidthPoints / 1.5, 0))

		' Make sure that intPeakHalfWidthPoints is at least NOISE_ESTIMATE_DATACOUNT_MINIMUM
		If intPeakHalfWidthPoints < NOISE_ESTIMATE_DATACOUNT_MINIMUM Then
			intPeakHalfWidthPoints = NOISE_ESTIMATE_DATACOUNT_MINIMUM
		End If

		' Copy the peak base indices
		intIndexBaseLeft = udtSICPeak.IndexBaseLeft
		intIndexBaseRight = udtSICPeak.IndexBaseRight

		' Define IndexStart and IndexEnd, making sure that intPeakHalfWidthPoints is no larger than NOISE_ESTIMATE_DATACOUNT_MAXIMUM
		intIndexStart = intIndexBaseLeft - Math.Min(intPeakHalfWidthPoints, NOISE_ESTIMATE_DATACOUNT_MAXIMUM)
		intIndexEnd = udtSICPeak.IndexBaseRight + Math.Min(intPeakHalfWidthPoints, NOISE_ESTIMATE_DATACOUNT_MAXIMUM)

		If intIndexStart < 0 Then intIndexStart = 0
		If intIndexEnd >= intDatacount Then intIndexEnd = intDatacount - 1

		' Compare intIndexStart to udtSICPeak.PreviousPeakFWHMPointRight
		' If it is less than .PreviousPeakFWHMPointRight, then update accordingly
		If intIndexStart < udtSICPeak.PreviousPeakFWHMPointRight AndAlso _
		   udtSICPeak.PreviousPeakFWHMPointRight < udtSICPeak.IndexMax Then
			' Update intIndexStart to be at PreviousPeakFWHMPointRight
			intIndexStart = udtSICPeak.PreviousPeakFWHMPointRight

			If intIndexStart < 0 Then intIndexStart = 0

			' If not enough points, then alternately shift intIndexStart to the left 1 point and 
			'  intIndexBaseLeft to the right one point until we do have enough points
			blnShiftLeft = True
			Do While intIndexBaseLeft - intIndexStart + 1 < NOISE_ESTIMATE_DATACOUNT_MINIMUM
				If blnShiftLeft Then
					If intIndexStart > 0 Then intIndexStart -= 1
				Else
					If intIndexBaseLeft < udtSICPeak.IndexMax Then intIndexBaseLeft += 1
				End If
				If intIndexStart <= 0 AndAlso intIndexBaseLeft >= udtSICPeak.IndexMax Then
					Exit Do
				Else
					blnShiftLeft = Not blnShiftLeft
				End If
			Loop
		End If

		' Compare intIndexEnd to udtSICPeak.NextPeakFWHMPointLeft
		' If it is greater than .NextPeakFWHMPointLeft, then update accordingly
		If intIndexEnd >= udtSICPeak.NextPeakFWHMPointLeft AndAlso _
		   udtSICPeak.NextPeakFWHMPointLeft > udtSICPeak.IndexMax Then
			intIndexEnd = udtSICPeak.NextPeakFWHMPointLeft

			If intIndexEnd >= intDatacount Then intIndexEnd = intDatacount - 1

			' If not enough points, then alternately shift intIndexEnd to the right 1 point and 
			'  intIndexBaseRight to the left one point until we do have enough points
			blnShiftLeft = False
			Do While intIndexEnd - intIndexBaseRight + 1 < NOISE_ESTIMATE_DATACOUNT_MINIMUM
				If blnShiftLeft Then
					If intIndexBaseRight > udtSICPeak.IndexMax Then intIndexBaseRight -= 1
				Else
					If intIndexEnd < intDatacount - 1 Then intIndexEnd += 1
				End If
				If intIndexBaseRight <= udtSICPeak.IndexMax AndAlso intIndexEnd >= intDatacount - 1 Then
					Exit Do
				Else
					blnShiftLeft = Not blnShiftLeft
				End If
			Loop
		End If

		With udtSICPeak
			blnSuccess = ComputeAverageNoiseLevelExcludingRegion(intDatacount, sngData, intIndexStart, intIndexEnd, intIndexBaseLeft, intIndexBaseRight, udtBaselineNoiseOptions, .BaselineNoiseStats)

			' Assure that .NoiseLevel is >= .MinimumBaselineNoiseLevel
			With .BaselineNoiseStats
				If .NoiseLevel < Math.Max(1, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel) Then
					.NoiseLevel = Math.Max(1, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel)
					.NoiseStDev = 0								' Set this to 0 since we have overridden .NoiseLevel
				End If
			End With
		End With


		Return blnSuccess

	End Function

	Public Function ComputeParentIonIntensity(ByVal intSICDataCount As Integer, ByVal SICScanNumbers() As Integer, ByVal SICData() As Single, ByRef udtSICPeak As udtSICStatsPeakType, ByVal intFragScanNumber As Integer) As Boolean

		' Determine the value for udtSICPeak.ParentIonIntensity
		' The goal is to determine the intensity that the SIC data has in one scan prior to udtSICPeak.IndexObserved
		' This intensity value may be an interpolated value between two observed SIC values

		Dim intX1, intX2 As Integer
		Dim sngY1, sngY2 As Single

		Dim blnSuccess As Boolean

		Try
			' Lookup the scan number and intensity of the SIC scan at udtSICPeak.Indexobserved
			intX1 = SICScanNumbers(udtSICPeak.IndexObserved)
			sngY1 = SICData(udtSICPeak.IndexObserved)

			If intX1 = intFragScanNumber - 1 Then
				' The fragmentation scan was the next scan after the SIC scan the data was observed in
				' We can use sngY1 for .ParentIonIntensity
				udtSICPeak.ParentIonIntensity = sngY1
			ElseIf intX1 >= intFragScanNumber Then
				' The fragmentation scan has the same scan number as the SIC scan just before it, or the SIC scan is greater than the fragmentation scan
				' This shouldn't normally happen, but we'll account for the possibility anyway
				' If the data file only has MS spectra and no MS/MS spectra, and if the parent ion is a custom M/Z value, then this code will be reached
				udtSICPeak.ParentIonIntensity = sngY1
			Else
				' We need to perform some interpolation to determine .ParentIonIntensity
				' Lookup the scan number and intensity of the next SIC scan
				If udtSICPeak.IndexObserved < intSICDataCount - 1 Then
					intX2 = SICScanNumbers(udtSICPeak.IndexObserved + 1)
					sngY2 = SICData(udtSICPeak.IndexObserved + 1)

					blnSuccess = InterpolateY(udtSICPeak.ParentIonIntensity, intX1, intX2, sngY1, sngY2, intFragScanNumber - 1)
					If Not blnSuccess Then
						' Interpolation failed; use sngY1
						udtSICPeak.ParentIonIntensity = sngY1
					End If
				Else
					' Cannot interpolate; we'll have to use sngY1 as .ParentIonIntensity
					udtSICPeak.ParentIonIntensity = sngY1
				End If
			End If

			blnSuccess = True

		Catch ex As Exception
			' Ignore errors here
			blnSuccess = False
		End Try

		Return blnSuccess

	End Function

	Private Function ComputeSICPeakArea(ByVal SICScanNumbers() As Integer, ByVal SICData() As Single, ByRef udtSICPeak As udtSICStatsPeakType) As Boolean
		' The calling function must populate udtSICPeak.IndexMax, udtSICPeak.IndexBaseLeft, and udtSICPeak.IndexBaseRight

		Dim intAreaDataCount As Integer
		Dim intAreaDataBaseIndex As Integer

		Dim intScanNumbers() As Integer
		Dim sngIntensities() As Single

		Dim sngIntensityThreshold As Single

		Dim intDataIndex As Integer

		Dim intScanDelta As Integer
		Dim intAvgScanInterval As Integer

		Dim intIndexPointer As Integer

		Try

			' Compute the peak area

			' Copy the matching data from the source arrays to intScanNumbers() and sngIntensities
			' When copying, assure that the first and last points have an intensity of 0

			' We're reserving extra space in case we need to prepend or append a minimum value
			ReDim intScanNumbers(udtSICPeak.IndexBaseRight - udtSICPeak.IndexBaseLeft + 2)
			ReDim sngIntensities(udtSICPeak.IndexBaseRight - udtSICPeak.IndexBaseLeft + 2)

			' Define an intensity threshold of 5% of MaximumIntensity
			' If the peak data is not flanked by points <= sngIntensityThreshold, then we'll add them
			sngIntensityThreshold = CSng(SICData(udtSICPeak.IndexMax) * 0.05)

			' Estimate the average scan interval between each data point
			intAvgScanInterval = CInt(Math.Round(ComputeAvgScanInterval(SICScanNumbers, udtSICPeak.IndexBaseLeft, udtSICPeak.IndexBaseRight), 0))

			If SICData(udtSICPeak.IndexBaseLeft) > sngIntensityThreshold Then
				' Prepend an intensity data point of sngIntensityThreshold, with a scan number intAvgScanInterval less than the first scan number for the actual peak data
				intScanNumbers(0) = SICScanNumbers(udtSICPeak.IndexBaseLeft) - intAvgScanInterval
				sngIntensities(0) = sngIntensityThreshold
				'sngIntensitiesSmoothed(0) = sngIntensityThreshold
				intAreaDataBaseIndex = 1
			Else
				intAreaDataBaseIndex = 0
			End If

			' Populate intScanNumbers() and sngIntensities()
			For intDataIndex = udtSICPeak.IndexBaseLeft To udtSICPeak.IndexBaseRight
				intIndexPointer = intDataIndex - udtSICPeak.IndexBaseLeft + intAreaDataBaseIndex
				intScanNumbers(intIndexPointer) = SICScanNumbers(intDataIndex)
				sngIntensities(intIndexPointer) = SICData(intDataIndex)
				'sngIntensitiesSmoothed(intIndexPointer) = udtSmoothedYDataSubset.Data(intDataIndex - udtSmoothedYDataSubset.DataStartIndex)
				'If sngIntensitiesSmoothed(intIndexPointer) < 0 Then sngIntensitiesSmoothed(intIndexPointer) = 0
			Next intDataIndex
			intAreaDataCount = udtSICPeak.IndexBaseRight - udtSICPeak.IndexBaseLeft + 1 + intAreaDataBaseIndex

			If SICData(udtSICPeak.IndexBaseRight) > sngIntensityThreshold Then
				' Append an intensity data point of sngIntensityThreshold, with a scan number intAvgScanInterval more than the last scan number for the actual peak data
				intDataIndex = udtSICPeak.IndexBaseRight - udtSICPeak.IndexBaseLeft + intAreaDataBaseIndex + 1
				intScanNumbers(intDataIndex) = SICScanNumbers(udtSICPeak.IndexBaseRight) + intAvgScanInterval
				sngIntensities(intDataIndex) = sngIntensityThreshold
				intAreaDataCount += 1
				'sngIntensitiesSmoothed(intDataIndex) = sngIntensityThreshold
			End If

			' Compute the area
			' Note that we're using real data for this and not smoothed data
			' Also note that we're using raw data for the peak area (not baseline corrected values)
			udtSICPeak.Area = 0
			For intDataIndex = 0 To intAreaDataCount - 2
				' Use the Trapezoid area formula to compute the area slice to add to udtSICPeak.Area
				' Area = 0.5 * DeltaX * (Y1 + Y2)
				intScanDelta = intScanNumbers(intDataIndex + 1) - intScanNumbers(intDataIndex)
				udtSICPeak.Area += CSng(0.5 * intScanDelta * (sngIntensities(intDataIndex) + sngIntensities(intDataIndex + 1)))
			Next intDataIndex

			If udtSICPeak.Area < 0 Then
				udtSICPeak.Area = 0
			End If

		Catch ex As Exception
			LogErrors("clsMASICPeakFinder->ComputeSICPeakArea", "Error computing area", ex, True, False, True)
			Return False
		End Try

		Return True

	End Function

	Private Function ComputeAvgScanInterval(ByVal intScanData() As Integer, ByVal intDataIndexStart As Integer, ByVal intDataIndexEnd As Integer) As Single

		Dim sngScansPerPoint As Single

		Try
			' Estimate the average scan interval between each data point
			If intDataIndexEnd >= intDataIndexStart Then
				sngScansPerPoint = CSng((intScanData(intDataIndexEnd) - intScanData(intDataIndexStart)) / (intDataIndexEnd - intDataIndexStart + 1))
				If sngScansPerPoint < 1 Then sngScansPerPoint = 1
			Else
				sngScansPerPoint = 1
			End If
		Catch ex As Exception
			sngScansPerPoint = 1
		End Try

		Return sngScansPerPoint

	End Function

	Private Function ComputeStatisticalMomentsStats(ByVal intSICDataCount As Integer, ByVal SICScanNumbers() As Integer, ByVal SICData() As Single, ByRef udtSmoothedYDataSubset As udtSmoothedYDataSubsetType, ByRef udtSICPeak As udtSICStatsPeakType) As Boolean
		' The calling function must populate udtSICPeak.IndexMax, udtSICPeak.IndexBaseLeft, and udtSICPeak.IndexBaseRight
		' Returns True if success; false if an error or less than 3 usable data points

		Const ALLOW_NEGATIVE_VALUES As Boolean = False
		Const USE_SMOOTHED_DATA As Boolean = True
		Const DEFAULT_MINIMUM_DATA_COUNT As Integer = 5

		Dim intDataIndex As Integer
		Dim intIndexPointer As Integer
		Dim intSmoothedDataPointer As Integer

		Dim intValidDataIndexLeft As Integer
		Dim intValidDataIndexRight As Integer

		Dim intScanDelta As Integer
		Dim intScanNumberInterpolate As Integer
		Dim intAvgScanInterval As Integer

		Dim intDataCount As Integer
		Dim intMinimumDataCount As Integer
		Dim intScanNumbers() As Integer			' Contains values from SICScanNumbers()
		Dim sngIntensities() As Single			' Contains values from sngIntensities() subtracted by the baseline noise level; if the result is less than 0, then will contain 0

		Dim sngMaximumBaselineAdjustedIntensity As Single
		Dim intIndexMaximumIntensity As Integer

		Dim sngIntensityThreshold As Single
		Dim sngInterpolatedIntensity As Single

		Dim dblArea As Double
		Dim dblCenterOfMassDecimal As Double

		Dim dblMoment1Sum As Double
		Dim dblMoment2Sum As Double
		Dim dblMoment3Sum As Double

		Dim blnUseRawDataAroundMaximum As Boolean


		' Note that we're using baseline corrected intensity values for the statistical moments
		' However, it is important that we use continuous, positive data for computing statistical moments

		Try
			' Initialize to default values
			With udtSICPeak.StatisticalMoments
				Try
					If udtSICPeak.IndexMax >= 0 AndAlso udtSICPeak.IndexMax < intDataCount Then
						.CenterOfMassScan = SICScanNumbers(udtSICPeak.IndexMax)
					End If
				Catch ex As Exception
					' Ignore errors here
				End Try
				.Area = 0
				.StDev = 0
				.Skew = 0
				.KSStat = 0
				.DataCountUsed = 0
			End With

			intDataCount = udtSICPeak.IndexBaseRight - udtSICPeak.IndexBaseLeft + 1
			If intDataCount < 1 Then
				' Do not continue if less than one point across the peak
				Return False
			End If

			' When reserving memory for these arrays, include room to add a minimum value at the beginning and end of the data, if needed
			' Also, reserve space for a minimum of 5 elements
			intMinimumDataCount = DEFAULT_MINIMUM_DATA_COUNT
			If intMinimumDataCount > intDataCount Then
				intMinimumDataCount = 3
			End If

			ReDim intScanNumbers(Math.Max(intDataCount, intMinimumDataCount) + 1)
			ReDim sngIntensities(intScanNumbers.Length - 1)
			blnUseRawDataAroundMaximum = False

			' Populate intScanNumbers() and sngIntensities()
			' Simultaneously, determine the maximum intensity
			sngMaximumBaselineAdjustedIntensity = 0
			intIndexMaximumIntensity = 0

			If USE_SMOOTHED_DATA Then
				intDataCount = 0
				For intDataIndex = udtSICPeak.IndexBaseLeft To udtSICPeak.IndexBaseRight
					intSmoothedDataPointer = intDataIndex - udtSmoothedYDataSubset.DataStartIndex
					If intSmoothedDataPointer >= 0 AndAlso intSmoothedDataPointer < udtSmoothedYDataSubset.DataCount Then
						intScanNumbers(intDataCount) = SICScanNumbers(intDataIndex)
						sngIntensities(intDataCount) = BaselineAdjustIntensity(udtSmoothedYDataSubset.Data(intSmoothedDataPointer), udtSICPeak.BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
						If sngIntensities(intDataCount) > sngMaximumBaselineAdjustedIntensity Then
							sngMaximumBaselineAdjustedIntensity = sngIntensities(intDataCount)
							intIndexMaximumIntensity = intDataCount
						End If
						intDataCount += 1
					End If
				Next intDataIndex
			Else
				intDataCount = 0
				For intDataIndex = udtSICPeak.IndexBaseLeft To udtSICPeak.IndexBaseRight
					intScanNumbers(intDataCount) = SICScanNumbers(intDataIndex)
					sngIntensities(intDataCount) = BaselineAdjustIntensity(SICData(intDataIndex), udtSICPeak.BaselineNoiseStats.NoiseLevel, ALLOW_NEGATIVE_VALUES)
					If sngIntensities(intDataCount) > sngMaximumBaselineAdjustedIntensity Then
						sngMaximumBaselineAdjustedIntensity = sngIntensities(intDataCount)
						intIndexMaximumIntensity = intDataCount
					End If
					intDataCount += 1
				Next intDataIndex
			End If

			' Define an intensity threshold of 10% of MaximumBaselineAdjustedIntensity
			sngIntensityThreshold = CSng(sngMaximumBaselineAdjustedIntensity * 0.1)
			If sngIntensityThreshold < 1 Then sngIntensityThreshold = 1

			' Step left from intIndexMaximumIntensity to find the first data point < sngIntensityThreshold
			' Note that the final data will include one data point less than sngIntensityThreshold at the beginning and end of the data
			intValidDataIndexLeft = intIndexMaximumIntensity
			Do While intValidDataIndexLeft > 0 AndAlso sngIntensities(intValidDataIndexLeft) >= sngIntensityThreshold
				intValidDataIndexLeft -= 1
			Loop

			' Step right from intIndexMaximumIntensity to find the first data point < sngIntensityThreshold
			intValidDataIndexRight = intIndexMaximumIntensity
			Do While intValidDataIndexRight < intDataCount - 1 AndAlso sngIntensities(intValidDataIndexRight) >= sngIntensityThreshold
				intValidDataIndexRight += 1
			Loop

			If intValidDataIndexLeft > 0 OrElse intValidDataIndexRight < intDataCount - 1 Then
				' Shrink the arrays to only retain the data centered around intIndexMaximumIntensity and 
				'  having and intensity >= sngIntensityThreshold, though one additional data point is retained at the beginning and end of the data
				For intDataIndex = intValidDataIndexLeft To intValidDataIndexRight
					intIndexPointer = intDataIndex - intValidDataIndexLeft
					intScanNumbers(intIndexPointer) = intScanNumbers(intDataIndex)
					sngIntensities(intIndexPointer) = sngIntensities(intDataIndex)
				Next intDataIndex
				intDataCount = intValidDataIndexRight - intValidDataIndexLeft + 1
			End If

			If intDataCount < intMinimumDataCount Then
				blnUseRawDataAroundMaximum = True
			Else
				' Remove the contiguous data from the left that is < sngIntensityThreshold, retaining one point < sngIntensityThreshold
				' Due to the algorithm used to find the contiguous data cenetered around the peak maximum, this will typically have no effect
				intValidDataIndexLeft = 0
				Do While intValidDataIndexLeft < intDataCount - 1 AndAlso sngIntensities(intValidDataIndexLeft + 1) < sngIntensityThreshold
					intValidDataIndexLeft += 1
				Loop

				If intValidDataIndexLeft >= intDataCount - 1 Then
					' All of the data is <= sngIntensityThreshold
					blnUseRawDataAroundMaximum = True
				Else
					If intValidDataIndexLeft > 0 Then
						' Shrink the array to remove the values at the beginning that are < sngIntensityThreshold, retaining one point < sngIntensityThreshold
						' Due to the algorithm used to find the contiguous data cenetered around the peak maximum, this code will typically never be reached
						For intDataIndex = intValidDataIndexLeft To intDataCount - 1
							intIndexPointer = intDataIndex - intValidDataIndexLeft
							intScanNumbers(intIndexPointer) = intScanNumbers(intDataIndex)
							sngIntensities(intIndexPointer) = sngIntensities(intDataIndex)
						Next intDataIndex
						intDataCount -= intValidDataIndexLeft
					End If

					' Remove the contiguous data from the right that is < sngIntensityThreshold, retaining one point < sngIntensityThreshold
					' Due to the algorithm used to find the contiguous data cenetered around the peak maximum, this will typically have no effect
					intValidDataIndexRight = intDataCount - 1
					Do While intValidDataIndexRight > 0 AndAlso sngIntensities(intValidDataIndexRight - 1) < sngIntensityThreshold
						intValidDataIndexRight -= 1
					Loop

					If intValidDataIndexRight < intDataCount - 1 Then
						' Shrink the array to remove the values at the end that are < sngIntensityThreshold, retaining one point < sngIntensityThreshold
						' Due to the algorithm used to find the contiguous data cenetered around the peak maximum, this code will typically never be reached
						intDataCount = intValidDataIndexRight + 1
					End If

					' Estimate the average scan interval between the data points in intScanNumbers
					intAvgScanInterval = CInt(Math.Round(ComputeAvgScanInterval(intScanNumbers, 0, intDataCount - 1), 0))

					' Make sure that sngIntensities(0) is <= sngIntensityThreshold
					If sngIntensities(0) > sngIntensityThreshold Then
						' Prepend a data point with intensity sngIntensityThreshold and with a scan number 1 less than the first scan number in the valid data
						For intDataIndex = intDataCount To 1 Step -1
							intScanNumbers(intDataIndex) = intScanNumbers(intDataIndex - 1)
							sngIntensities(intDataIndex) = sngIntensities(intDataIndex - 1)
						Next intDataIndex
						intScanNumbers(0) = intScanNumbers(1) - intAvgScanInterval
						sngIntensities(0) = sngIntensityThreshold
						intDataCount += 1
					End If

					' Make sure that sngIntensities(intDataCount-1) is <= sngIntensityThreshold
					If sngIntensities(intDataCount - 1) > sngIntensityThreshold Then
						' Append a data point with intensity sngIntensityThreshold and with a scan number 1 more than the last scan number in the valid data
						intScanNumbers(intDataCount) = intScanNumbers(intDataCount - 1) + intAvgScanInterval
						sngIntensities(intDataCount) = sngIntensityThreshold
						intDataCount += 1
					End If

				End If
			End If

			If blnUseRawDataAroundMaximum OrElse intDataCount < intMinimumDataCount Then
				' Populate intScanNumbers() and sngIntensities() with the five data points centered around udtSICPeak.IndexMax
				If USE_SMOOTHED_DATA Then
					intValidDataIndexLeft = udtSICPeak.IndexMax - CInt(Math.Floor(intMinimumDataCount / 2))
					If intValidDataIndexLeft < 0 Then intValidDataIndexLeft = 0
					intDataCount = 0
					For intDataIndex = intValidDataIndexLeft To Math.Min(intValidDataIndexLeft + intMinimumDataCount - 1, intSICDataCount - 1)
						intSmoothedDataPointer = intDataIndex - udtSmoothedYDataSubset.DataStartIndex
						If intSmoothedDataPointer >= 0 AndAlso intSmoothedDataPointer < udtSmoothedYDataSubset.DataCount Then
							If udtSmoothedYDataSubset.Data(intSmoothedDataPointer) > 0 Then
								intScanNumbers(intDataCount) = SICScanNumbers(intDataIndex)
								sngIntensities(intDataCount) = udtSmoothedYDataSubset.Data(intSmoothedDataPointer)
								intDataCount += 1
							End If
						End If
					Next intDataIndex
				Else
					intValidDataIndexLeft = udtSICPeak.IndexMax - CInt(Math.Floor(intMinimumDataCount / 2))
					If intValidDataIndexLeft < 0 Then intValidDataIndexLeft = 0
					intDataCount = 0
					For intDataIndex = intValidDataIndexLeft To Math.Min(intValidDataIndexLeft + intMinimumDataCount - 1, intSICDataCount - 1)
						If SICData(intDataIndex) > 0 Then
							intScanNumbers(intDataCount) = SICScanNumbers(intDataIndex)
							sngIntensities(intDataCount) = SICData(intDataIndex)
							intDataCount += 1
						End If
					Next intDataIndex
				End If

				If intDataCount < 3 Then
					' We don't even have 3 positive values in the raw data; do not continue
					Return False
				End If
			End If

			' Step through sngIntensities and interpolate across gaps with intensities of 0
			' Due to the algorithm used to find the contiguous data cenetered around the peak maximum, this will typically have no effect
			intDataIndex = 1
			Do While intDataIndex < intDataCount - 1
				If sngIntensities(intDataIndex) <= 0 Then
					' Current point has an intensity of 0
					' Find the next positive point
					intValidDataIndexLeft = intDataIndex + 1
					Do While intValidDataIndexLeft < intDataCount AndAlso sngIntensities(intValidDataIndexLeft) <= 0
						intValidDataIndexLeft += 1
					Loop

					' Interpolate between intDataIndex-1 and intValidDataIndexLeft
					For intIndexPointer = intDataIndex To intValidDataIndexLeft - 1
						If InterpolateY(sngInterpolatedIntensity, intScanNumbers(intDataIndex - 1), intScanNumbers(intValidDataIndexLeft), sngIntensities(intDataIndex - 1), sngIntensities(intValidDataIndexLeft), intScanNumbers(intIndexPointer)) Then
							sngIntensities(intIndexPointer) = sngInterpolatedIntensity
						End If
					Next intIndexPointer
					intDataIndex = intValidDataIndexLeft + 1
				Else
					intDataIndex += 1
				End If
			Loop

			' Compute the zeroth moment (m0)
			dblArea = 0
			For intDataIndex = 0 To intDataCount - 2
				' Use the Trapezoid area formula to compute the area slice to add to dblArea
				' Area = 0.5 * DeltaX * (Y1 + Y2)
				intScanDelta = intScanNumbers(intDataIndex + 1) - intScanNumbers(intDataIndex)
				dblArea += 0.5 * intScanDelta * (sngIntensities(intDataIndex) + sngIntensities(intDataIndex + 1))
			Next intDataIndex

			' For the first moment (m1), need to sum: intensity times scan number
			' For each of the moments, need to subtract intScanNumbers(0) from the scan numbers since statistical moments calcs are skewed if the first X value is not zero
			' When ScanDelta is > 1, then need to interpolate

			dblMoment1Sum = (intScanNumbers(0) - intScanNumbers(0)) * sngIntensities(0)
			For intDataIndex = 1 To intDataCount - 1
				dblMoment1Sum += (intScanNumbers(intDataIndex) - intScanNumbers(0)) * sngIntensities(intDataIndex)

				intScanDelta = intScanNumbers(intDataIndex) - intScanNumbers(intDataIndex - 1)
				If intScanDelta > 1 Then
					' Data points are more than 1 scan apart; need to interpolate values
					' However, no need to interpolate if both intensity values are 0
					If sngIntensities(intDataIndex - 1) > 0 OrElse sngIntensities(intDataIndex) > 0 Then
						For intScanNumberInterpolate = intScanNumbers(intDataIndex - 1) + 1 To intScanNumbers(intDataIndex) - 1
							' Use InterpolateY() to fill in the scans between intDataIndex-1 and intDataIndex
							If InterpolateY(sngInterpolatedIntensity, intScanNumbers(intDataIndex - 1), intScanNumbers(intDataIndex), sngIntensities(intDataIndex - 1), sngIntensities(intDataIndex), intScanNumberInterpolate) Then
								dblMoment1Sum += (intScanNumberInterpolate - intScanNumbers(0)) * sngInterpolatedIntensity
							End If
						Next intScanNumberInterpolate
					End If
				End If
			Next intDataIndex

			If dblArea <= 0 Then
				' Cannot compute the center of mass; use the scan at .IndexMax instead
				With udtSICPeak
					intIndexPointer = .IndexMax - .IndexBaseLeft
					If intIndexPointer >= 0 AndAlso intIndexPointer < intScanNumbers.Length Then
						dblCenterOfMassDecimal = intScanNumbers(intIndexPointer)
					End If
					.StatisticalMoments.CenterOfMassScan = CInt(Math.Round(dblCenterOfMassDecimal, 0))
					.StatisticalMoments.DataCountUsed = 1
				End With
			Else
				' Area is positive; compute the center of mass
				With udtSICPeak
					dblCenterOfMassDecimal = dblMoment1Sum / dblArea + intScanNumbers(0)
					With .StatisticalMoments
						.Area = CSng(Math.Min(Single.MaxValue, dblArea))
						.CenterOfMassScan = CInt(Math.Round(dblCenterOfMassDecimal, 0))
						.DataCountUsed = intDataCount
					End With
				End With

				' For the second moment (m2), need to sum: (ScanNumber - m1)^2 * Intensity
				' For the third moment (m3), need to sum: (ScanNumber - m1)^3 * Intensity
				' When ScanDelta is > 1, then need to interpolate
				dblMoment2Sum = ((intScanNumbers(0) - dblCenterOfMassDecimal) ^ 2) * sngIntensities(0)
				dblMoment3Sum = ((intScanNumbers(0) - dblCenterOfMassDecimal) ^ 3) * sngIntensities(0)
				For intDataIndex = 1 To intDataCount - 1
					dblMoment2Sum += ((intScanNumbers(intDataIndex) - dblCenterOfMassDecimal) ^ 2) * sngIntensities(intDataIndex)
					dblMoment3Sum += ((intScanNumbers(intDataIndex) - dblCenterOfMassDecimal) ^ 3) * sngIntensities(intDataIndex)

					intScanDelta = intScanNumbers(intDataIndex) - intScanNumbers(intDataIndex - 1)
					If intScanDelta > 1 Then
						' Data points are more than 1 scan apart; need to interpolate values
						' However, no need to interpolate if both intensity values are 0
						If sngIntensities(intDataIndex - 1) > 0 OrElse sngIntensities(intDataIndex) > 0 Then
							For intScanNumberInterpolate = intScanNumbers(intDataIndex - 1) + 1 To intScanNumbers(intDataIndex) - 1
								' Use InterpolateY() to fill in the scans between intDataIndex-1 and intDataIndex
								If InterpolateY(sngInterpolatedIntensity, intScanNumbers(intDataIndex - 1), intScanNumbers(intDataIndex), sngIntensities(intDataIndex - 1), sngIntensities(intDataIndex), intScanNumberInterpolate) Then
									dblMoment2Sum += ((intScanNumberInterpolate - dblCenterOfMassDecimal) ^ 2) * sngInterpolatedIntensity
									dblMoment3Sum += ((intScanNumberInterpolate - dblCenterOfMassDecimal) ^ 3) * sngInterpolatedIntensity
								End If
							Next intScanNumberInterpolate
						End If
					End If
				Next intDataIndex

				With udtSICPeak.StatisticalMoments
					.StDev = CSng(Math.Sqrt(dblMoment2Sum / dblArea))

					' dblThirdMoment = dblMoment3Sum / dblArea
					' skew = dblThirdMoment / sigma^3
					' skew = (dblMoment3Sum / dblArea) / sigma^3
					If .StDev > 0 Then
						.Skew = CSng((dblMoment3Sum / dblArea) / (.StDev ^ 3))
						If Math.Abs(.Skew) < 0.0001 Then
							.Skew = 0
						End If
					Else
						.Skew = 0
					End If
				End With
			End If

			Const blnUseStatMomentsStats As Boolean = True
			Dim peakMean As Single
			Dim peakStDev As Double

			With udtSICPeak.StatisticalMoments
				If blnUseStatMomentsStats Then
					peakMean = .CenterOfMassScan
					peakStDev = .StDev
				Else
					peakMean = SICScanNumbers(udtSICPeak.IndexMax)
					' FWHM / 2.35482 = FWHM / (2 * Sqrt(2 * Ln(2)))
					peakStDev = udtSICPeak.FWHMScanWidth / 2.35482
				End If
				.KSStat = CSng(ComputeKSStatistic(intDataCount, intScanNumbers, sngIntensities, peakMean, peakStDev))
			End With

		Catch ex As Exception
			LogErrors("clsMASICPeakFinder->ComputeStatisticalMomentsStats", "Error computing statistical momements", ex, True, False, True)
			Return False
		End Try

		Return True

	End Function

	Public Shared Function ComputeSignalToNoise(ByVal sngSignal As Single, ByVal sngNoiseThresholdIntensity As Single) As Single

		If sngNoiseThresholdIntensity > 0 Then
			Return sngSignal / sngNoiseThresholdIntensity
		Else
			Return 0
		End If

	End Function

	Public Function ComputeTrimmedNoiseLevel(ByVal sngData() As Single, ByVal intIndexStart As Integer, ByVal intIndexEnd As Integer, ByVal udtBaselineNoiseOptions As udtBaselineNoiseOptionsType, ByVal blnIgnoreNonPositiveData As Boolean, ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType) As Boolean
		' Computes a trimmed mean or trimmed median using the low intensity data up to udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage
		' Additionally, computes a full median using all data in sngData
		' If blnIgnoreNonPositiveData is True, then removes data from sngData() <= 0 and <= .MinimumBaselineNoiseLevel
		' Returns True if success, False if error (or no data in sngData)

		' Note: Replaces values of 0 with the minimum positive value in sngData()
		' Note: You cannot use sngData.Length to determine the length of the array; use intDataCount

		Dim intDataSortedCount As Integer
		Dim sngDataSorted() As Single			' Note: You cannot use sngDataSorted.Length to determine the length of the array; use intIndexStart and intIndexEnd to find the limits

		Dim dblIntensityThreshold As Double
		Dim dblSum As Double

		Dim intIndex As Integer
		Dim intValidDataCount As Integer

		Dim intCountSummed As Integer

		' Initialize udtBaselineNoiseStats
		InitializeBaselineNoiseStats(udtBaselineNoiseStats, udtBaselineNoiseOptions.MinimumBaselineNoiseLevel, udtBaselineNoiseOptions.BaselineNoiseMode)

		If sngData Is Nothing OrElse intIndexEnd - intIndexStart < 0 Then
			Return False
		End If

		' Copy the data into sngDataSorted
		intDataSortedCount = intIndexEnd - intIndexStart + 1
		ReDim sngDataSorted(intDataSortedCount - 1)

		If intIndexStart = 0 Then
			Array.Copy(sngData, sngDataSorted, intDataSortedCount)
		Else
			For intIndex = intIndexStart To intIndexEnd
				sngDataSorted(intIndex - intIndexStart) = sngData(intIndex)
			Next intIndex
		End If

		' Sort the array
		Array.Sort(sngDataSorted)

		If blnIgnoreNonPositiveData Then
			' Remove data with a value <= 0 

			If sngDataSorted(0) <= 0 Then
				intValidDataCount = 0
				For intIndex = 0 To intDataSortedCount - 1
					If sngDataSorted(intIndex) > 0 Then
						sngDataSorted(intValidDataCount) = sngDataSorted(intIndex)
						intValidDataCount += 1
					End If
				Next intIndex

				If intValidDataCount < intDataSortedCount Then
					intDataSortedCount = intValidDataCount
				End If

				' Check for no data remaining
				If intDataSortedCount <= 0 Then
					Return False
				End If
			End If
		End If

		' Look for the minimum positive value and replace all data in sngDataSorted with that value
		Dim sngMinimumPositiveValue = ReplaceSortedDataWithMinimumPositiveValue(intDataSortedCount, sngDataSorted)

		Select Case udtBaselineNoiseOptions.BaselineNoiseMode
			Case eNoiseThresholdModes.TrimmedMeanByAbundance, eNoiseThresholdModes.TrimmedMeanByCount

				If udtBaselineNoiseOptions.BaselineNoiseMode = eNoiseThresholdModes.TrimmedMeanByAbundance Then
					' Average the data that has intensity values less than
					'  Minimum + udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage * (Maximum - Minimum)
					With udtBaselineNoiseOptions
						dblIntensityThreshold = sngDataSorted(0) + .TrimmedMeanFractionLowIntensityDataToAverage * (sngDataSorted(intDataSortedCount - 1) - sngDataSorted(0))
					End With

					' Initialize intCountSummed to intDataSortedCount for now, in case all data is within the intensity threshold
					intCountSummed = intDataSortedCount
					dblSum = 0
					For intIndex = 0 To intDataSortedCount - 1
						If sngDataSorted(intIndex) <= dblIntensityThreshold Then
							dblSum += sngDataSorted(intIndex)
						Else
							' Update intCountSummed
							intCountSummed = intIndex
							Exit For
						End If
					Next intIndex
					intIndexEnd = intCountSummed - 1
				Else
					' eNoiseThresholdModes.TrimmedMeanByCount
					' Find the index of the data point at intDataSortedCount * udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage and
					'  average the data from the start to that index
					intIndexEnd = CInt(Math.Round((intDataSortedCount - 1) * udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage, 0))

					intCountSummed = intIndexEnd + 1
					dblSum = 0
					For intIndex = 0 To intIndexEnd
						dblSum += sngDataSorted(intIndex)
					Next intIndex
				End If

				If intCountSummed > 0 Then
					' Compute the average
					' Note that intCountSummed will be used below in the variance computation
					With udtBaselineNoiseStats
						.NoiseLevel = CSng(dblSum / intCountSummed)
						.PointsUsed = intCountSummed
					End With

					If intCountSummed > 1 Then
						' Compute the variance
						dblSum = 0
						For intIndex = 0 To intIndexEnd
							dblSum += (sngDataSorted(intIndex) - udtBaselineNoiseStats.NoiseLevel) ^ 2
						Next intIndex
						udtBaselineNoiseStats.NoiseStDev = CSng(Math.Sqrt(dblSum / (intCountSummed - 1)))
					Else
						udtBaselineNoiseStats.NoiseStDev = 0
					End If
				Else
					' No data to average; define the noise level to be the minimum intensity
					With udtBaselineNoiseStats
						.NoiseLevel = sngDataSorted(0)
						.NoiseStDev = 0
						.PointsUsed = 1
					End With
				End If

			Case eNoiseThresholdModes.TrimmedMedianByAbundance
				If udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage >= 1 Then
					intIndexEnd = intDataSortedCount - 1
				Else
					'Find the median of the data that has intensity values less than
					'  Minimum + udtBaselineNoiseOptions.TrimmedMeanFractionLowIntensityDataToAverage * (Maximum - Minimum)
					With udtBaselineNoiseOptions
						dblIntensityThreshold = sngDataSorted(0) + .TrimmedMeanFractionLowIntensityDataToAverage * (sngDataSorted(intDataSortedCount - 1) - sngDataSorted(0))
					End With

					' Find the first point with an intensity value <= dblIntensityThreshold
					intIndexEnd = intDataSortedCount - 1
					For intIndex = 1 To intDataSortedCount - 1
						If sngDataSorted(intIndex) > dblIntensityThreshold Then
							intIndexEnd = intIndex - 1
							Exit For
						End If
					Next intIndex
				End If

				If intIndexEnd Mod 2 = 0 Then
					' Even value
					udtBaselineNoiseStats.NoiseLevel = sngDataSorted(CInt(intIndexEnd / 2))
				Else
					' Odd value; average the values on either side of intIndexEnd/2
					intIndex = CInt((intIndexEnd - 1) / 2)
					If intIndex < 0 Then intIndex = 0
					dblSum = sngDataSorted(intIndex)

					intIndex += 1
					If intIndex = intDataSortedCount Then intIndex = intDataSortedCount - 1
					dblSum += sngDataSorted(intIndex)

					udtBaselineNoiseStats.NoiseLevel = CSng(dblSum / 2.0)
				End If

				' Compute the variance
				dblSum = 0
				For intIndex = 0 To intIndexEnd
					dblSum += (sngDataSorted(intIndex) - udtBaselineNoiseStats.NoiseLevel) ^ 2
				Next intIndex

				With udtBaselineNoiseStats
					intCountSummed = intIndexEnd + 1
					If intCountSummed > 0 Then
						.NoiseStDev = CSng(Math.Sqrt(dblSum / (intCountSummed - 1)))
					Else
						.NoiseStDev = 0
					End If
					.PointsUsed = intCountSummed
				End With
			Case Else
				' Unknown mode
				LogErrors("clsMASICPeakFinder->ComputeTrimmedNoiseLevel", "Unknown Noise Threshold Mode encountered: " & udtBaselineNoiseOptions.BaselineNoiseMode.ToString, Nothing, True, False)
				Return False
		End Select

		' Assure that .NoiseLevel is >= .MinimumBaselineNoiseLevel
		With udtBaselineNoiseStats
			If .NoiseLevel < udtBaselineNoiseOptions.MinimumBaselineNoiseLevel AndAlso udtBaselineNoiseOptions.MinimumBaselineNoiseLevel > 0 Then
				.NoiseLevel = udtBaselineNoiseOptions.MinimumBaselineNoiseLevel
				.NoiseStDev = 0								' Set this to 0 since we have overridden .NoiseLevel
			End If
		End With

		Return True

	End Function

	Private Shared Function ComputeWidthAtBaseUsingFWHM(ByRef udtSICPeak As udtSICStatsPeakType, ByVal SICScanNumbers() As Integer, ByVal SigmaValueForBase As Short) As Integer
		' Computes the width of the peak (in scans) using the FWHM value in udtSICPeak
		Dim intPeakWidthFullScans As Integer

		Try
			intPeakWidthFullScans = SICScanNumbers(udtSICPeak.IndexBaseRight) - SICScanNumbers(udtSICPeak.IndexBaseLeft) + 1
			Return ComputeWidthAtBaseUsingFWHM(udtSICPeak.FWHMScanWidth, intPeakWidthFullScans, SigmaValueForBase)
		Catch ex As Exception
			Return 0
		End Try

	End Function

	Private Shared Function ComputeWidthAtBaseUsingFWHM(ByVal intSICPeakFWHMScans As Integer, ByVal intSICPeakWidthFullScans As Integer, Optional ByVal SigmaValueForBase As Short = 4) As Integer
		' Computes the width of the peak (in scans) using the FWHM value
		' However, does not allow the width determined to be larger than intSICPeakWidthFullScans

		Dim intWidthAtBase As Integer
		Dim intSigmaBasedWidth As Integer

		If SigmaValueForBase < 4 Then SigmaValueForBase = 4

		If intSICPeakFWHMScans = 0 Then
			intWidthAtBase = intSICPeakWidthFullScans
		Else
			' Compute the peak width
			' Note: Sigma = FWHM / 2.35482 = FWHM / (2 * Sqrt(2 * Ln(2)))
			intSigmaBasedWidth = CInt(SigmaValueForBase * intSICPeakFWHMScans / 2.35482)

			If intSigmaBasedWidth <= 0 Then
				intWidthAtBase = intSICPeakWidthFullScans
			ElseIf intSICPeakWidthFullScans = 0 Then
				intWidthAtBase = intSigmaBasedWidth
			Else
				' Compare the sigma-based peak width to intSICPeakWidthFullScans
				' Assign the smaller of the two values to intWidthAtBase
				intWidthAtBase = Math.Min(intSigmaBasedWidth, intSICPeakWidthFullScans)
			End If
		End If

		Return intWidthAtBase

	End Function

	Private Function ConvertScanWidthToPoints(ByVal intPeakWidthBaseScans As Integer, ByRef udtSICPeak As udtSICStatsPeakType, ByVal SICScanNumbers() As Integer) As Integer
		' Convert from intPeakWidthFullScans to points; estimate number of scans per point to get this

		Dim sngScansPerPoint As Single

		sngScansPerPoint = ComputeAvgScanInterval(SICScanNumbers, udtSICPeak.IndexBaseLeft, udtSICPeak.IndexBaseRight)
		Return CInt(Math.Round(intPeakWidthBaseScans / sngScansPerPoint, 0))

	End Function

	Public Function FindMinimumPositiveValue(ByVal intDatacount As Integer, ByVal sngData() As Single, ByVal sngAbsoluteMinimumValue As Single) As Single
		' Note: Do not use sngData.Length to determine the length of the array; use intDataCount
		' However, if intDataCount is > sngData.Length then sngData.Length-1 will be used for the maximum index to examine

		Dim intIndex As Integer
		Dim sngMinimumPositiveValue As Single

		If intDatacount > sngData.Length Then
			intDatacount = sngData.Length
		End If

		' Find the minimum positive value in sngData
		sngMinimumPositiveValue = Single.MaxValue
		For intIndex = 0 To intDatacount - 1
			If sngData(intIndex) > 0 Then
				If sngData(intIndex) < sngMinimumPositiveValue Then
					sngMinimumPositiveValue = sngData(intIndex)
				End If
			End If
		Next intIndex
		If sngMinimumPositiveValue >= Single.MaxValue OrElse sngMinimumPositiveValue < sngAbsoluteMinimumValue Then
			sngMinimumPositiveValue = sngAbsoluteMinimumValue
		End If

		Return sngMinimumPositiveValue

	End Function

	Private Function FindPeaks(ByVal intDataCount As Integer, ByVal intScanNumbers() As Integer, ByVal sngIntensityData() As Single, ByRef intPeakIndexStart As Integer, ByRef intPeakIndexEnd As Integer, ByRef intPeakLocationIndex As Integer, ByRef intPreviousPeakFWHMPointRight As Integer, ByRef intNextPeakFWHMPointLeft As Integer, ByRef intShoulderCount As Integer, ByRef udtSmoothedYDataSubset As udtSmoothedYDataSubsetType, ByVal blnSIMDataPresent As Boolean, ByVal udtSICPeakFinderOptions As udtSICPeakFinderOptionsType, ByVal sngSICNoiseThresholdIntensity As Single, ByVal dblMinimumPotentialPeakArea As Double, ByVal blnReturnClosestPeak As Boolean) As Boolean
		' Returns True if a valid peak is found in sngIntensityData()
		' Otherwise, returns false
		'
		' When blnReturnClosestPeak = True, then intPeakLocationIndex should be populated with the "best guess" location of the peak in the intScanNumbers() and sngIntensityData() arrays
		'   The peak closest to intPeakLocationIndex will be the chosen peak, even if it is not the most intense peak found

		Const SMOOTHED_DATA_PADDING_COUNT As Integer = 2

		Dim objPeakDetector As New clsPeakDetection
		Dim blnTestingMinimumPeakWidth As Boolean

		Dim udtPeakData As udtFindPeaksDataType = New udtFindPeaksDataType
		Dim udtPeakDataSaved As udtFindPeaksDataType

		Dim dblMaximumIntensity, dblAreaSignalToNoise As Double
		Dim dblPotentialPeakArea, dblMaximumPotentialPeakArea As Double
		Dim queIntensityList As New Queue									' The queue is used to keep track of the most recent intensity values
		Dim intDataPointCountAboveThreshold As Integer

		Dim intIndex As Integer, intPeakIndexCompare As Integer
		Dim intSmoothedYDataEndIndex As Integer
		Dim intIndexMaxIntensity As Integer

		' ReSharper disable once NotAccessedVariable
		Dim intAdjacentIndex As Integer
		Dim intSmallestIndexDifference As Integer
		Dim sngAdjacentPeakIntensityThreshold As Single

		Dim blnValidPeakFound As Boolean

		Try
			udtPeakData.SourceDataCount = intDataCount
			If udtPeakData.SourceDataCount <= 1 Then
				' Only 1 or fewer points in sngIntensityData()
				' No point in looking for a "peak"
				intPeakIndexStart = 0
				intPeakIndexEnd = 0
				intPeakLocationIndex = 0
				Return False
			End If

			' Try to find the peak using the Peak Detector class
			' First need to populate .XData() and copy from sngIntensityData() to .YData()
			' At the same time, find dblMaximumIntensity and dblMaximumPotentialPeakArea

			' The peak finder class requires Arrays of type Double
			' Copy the data from the source arrays into udtPeakData.XData() and udtPeakData.YData()
			ReDim udtPeakData.XData(udtPeakData.SourceDataCount - 1)
			ReDim udtPeakData.YData(udtPeakData.SourceDataCount - 1)

			dblMaximumIntensity = sngIntensityData(0)
			dblMaximumPotentialPeakArea = 0
			intIndexMaxIntensity = 0

			' Initialize the intensity queue
			queIntensityList.Clear()
			dblPotentialPeakArea = 0
			intDataPointCountAboveThreshold = 0

			For intIndex = 0 To udtPeakData.SourceDataCount - 1
				udtPeakData.XData(intIndex) = intScanNumbers(intIndex)
				udtPeakData.YData(intIndex) = sngIntensityData(intIndex)
				If udtPeakData.YData(intIndex) > dblMaximumIntensity Then
					dblMaximumIntensity = udtPeakData.YData(intIndex)
					intIndexMaxIntensity = intIndex
				End If

				If sngIntensityData(intIndex) >= sngSICNoiseThresholdIntensity Then
					' Add this intensity to dblPotentialPeakArea
					dblPotentialPeakArea += sngIntensityData(intIndex)
					If queIntensityList.Count >= udtSICPeakFinderOptions.InitialPeakWidthScansMaximum Then
						' Decrement dblPotentialPeakArea by the oldest item in the queue
						dblPotentialPeakArea -= CDbl(queIntensityList.Dequeue())
					End If
					' Add this intensity to the queue
					queIntensityList.Enqueue(sngIntensityData(intIndex))

					If dblPotentialPeakArea > dblMaximumPotentialPeakArea Then
						dblMaximumPotentialPeakArea = dblPotentialPeakArea
					End If

					intDataPointCountAboveThreshold += 1

				End If
			Next intIndex

			' Determine the initial value for .PeakWidthPointsMinimum
			' We will use dblMaximumIntensity and sngMinimumPeakIntensity to compute a S/N value to help pick .PeakWidthPointsMinimum

			' Old: If udtSICPeakFinderOptions.SICNoiseThresholdIntensity < 1 Then udtSICPeakFinderOptions.SICNoiseThresholdIntensity = 1
			' Old: dblAreaSignalToNoise = dblMaximumIntensity / udtSICPeakFinderOptions.SICNoiseThresholdIntensity

			If dblMinimumPotentialPeakArea < 1 Then dblMinimumPotentialPeakArea = 1
			dblAreaSignalToNoise = dblMaximumPotentialPeakArea / dblMinimumPotentialPeakArea
			If dblAreaSignalToNoise < 1 Then dblAreaSignalToNoise = 1

			With udtPeakData

				If Math.Abs(udtSICPeakFinderOptions.ButterworthSamplingFrequency - 0) < Single.Epsilon Then udtSICPeakFinderOptions.ButterworthSamplingFrequency = 0.25

				.PeakWidthPointsMinimum = CInt(udtSICPeakFinderOptions.InitialPeakWidthScansScaler * Math.Log10(Math.Floor(dblAreaSignalToNoise)) * 10)

				' Assure that .InitialPeakWidthScansMaximum is no greater than .InitialPeakWidthScansMaximum 
				'  and no greater than intDataPointCountAboveThreshold/2 (rounded up)
				.PeakWidthPointsMinimum = Math.Min(.PeakWidthPointsMinimum, udtSICPeakFinderOptions.InitialPeakWidthScansMaximum)
				.PeakWidthPointsMinimum = Math.Min(.PeakWidthPointsMinimum, CInt(Math.Ceiling(intDataPointCountAboveThreshold / 2)))

				If .PeakWidthPointsMinimum > .SourceDataCount * 0.8 Then
					.PeakWidthPointsMinimum = CInt(Math.Floor(.SourceDataCount * 0.8))
				End If

				If .PeakWidthPointsMinimum < MINIMUM_PEAK_WIDTH Then .PeakWidthPointsMinimum = MINIMUM_PEAK_WIDTH


				' Save the original value for intPeakLocationIndex
				.OriginalPeakLocationIndex = intPeakLocationIndex
				.MaxAllowedUpwardSpikeFractionMax = udtSICPeakFinderOptions.MaxAllowedUpwardSpikeFractionMax

			End With

			Do
				If udtPeakData.PeakWidthPointsMinimum = MINIMUM_PEAK_WIDTH Then
					blnTestingMinimumPeakWidth = True
				Else
					blnTestingMinimumPeakWidth = False
				End If

				Try
					blnValidPeakFound = FindPeaksWork(objPeakDetector, intScanNumbers, udtPeakData, blnSIMDataPresent, udtSICPeakFinderOptions, blnTestingMinimumPeakWidth, blnReturnClosestPeak)
				Catch ex As Exception
					LogErrors("clsMASICPeakFinder->FindPeaks", "Error calling FindPeaksWork", ex, True, True, True)
					blnValidPeakFound = False
					Exit Do
				End Try

				If blnValidPeakFound Then
					With udtPeakData
						' For each peak, see if several zero intensity values are in a row in the raw data
						' If found, then narrow the peak to leave just one zero intensity value
						For intPeakIndexCompare = 0 To udtPeakData.PeakCount - 1

							Do While .PeakEdgesLeft(intPeakIndexCompare) < intDataCount - 1 AndAlso _
							   .PeakEdgesLeft(intPeakIndexCompare) < .PeakEdgesRight(intPeakIndexCompare)
								If Math.Abs(sngIntensityData(.PeakEdgesLeft(intPeakIndexCompare)) - 0) < Single.Epsilon AndAlso
								   Math.Abs(sngIntensityData(.PeakEdgesLeft(intPeakIndexCompare) + 1) - 0) < Single.Epsilon Then
									.PeakEdgesLeft(intPeakIndexCompare) += 1
								Else
									Exit Do
								End If
							Loop

							Do While .PeakEdgesRight(intPeakIndexCompare) > 0 AndAlso _
							   .PeakEdgesRight(intPeakIndexCompare) > .PeakEdgesLeft(intPeakIndexCompare)
								If Math.Abs(sngIntensityData(.PeakEdgesRight(intPeakIndexCompare)) - 0) < Single.Epsilon AndAlso
								   Math.Abs(sngIntensityData(.PeakEdgesRight(intPeakIndexCompare) - 1) - 0) < Single.Epsilon Then
									.PeakEdgesRight(intPeakIndexCompare) -= 1
								Else
									Exit Do
								End If
							Loop

							' Update the stats for the "official" peak
							intPeakLocationIndex = .PeakLocs(.BestPeakIndex)
							intPeakIndexStart = .PeakEdgesLeft(.BestPeakIndex)
							intPeakIndexEnd = .PeakEdgesRight(.BestPeakIndex)
						Next intPeakIndexCompare


						' Update the stats for the "official" peak
						intPeakLocationIndex = .PeakLocs(.BestPeakIndex)
						intPeakIndexStart = .PeakEdgesLeft(.BestPeakIndex)
						intPeakIndexEnd = .PeakEdgesRight(.BestPeakIndex)
					End With


					' Copy the smoothed Y data for the peak into udtSmoothedYDataSubset.Data()
					' Include some data to the left and right of the peak start and end
					' Additionally, be sure the smoothed data includes the data around the most intense data point in sngIntensityData
					udtSmoothedYDataSubset.DataStartIndex = intPeakIndexStart - SMOOTHED_DATA_PADDING_COUNT
					intSmoothedYDataEndIndex = intPeakIndexEnd + SMOOTHED_DATA_PADDING_COUNT

					' Make sure the maximum intensity point is included (with padding on either side)
					If intIndexMaxIntensity - SMOOTHED_DATA_PADDING_COUNT < udtSmoothedYDataSubset.DataStartIndex Then
						udtSmoothedYDataSubset.DataStartIndex = intIndexMaxIntensity - SMOOTHED_DATA_PADDING_COUNT
					End If

					If intIndexMaxIntensity + SMOOTHED_DATA_PADDING_COUNT > intSmoothedYDataEndIndex Then
						intSmoothedYDataEndIndex = intIndexMaxIntensity + SMOOTHED_DATA_PADDING_COUNT
					End If

					' Make sure the indices aren't out of range
					If udtSmoothedYDataSubset.DataStartIndex < 0 Then
						udtSmoothedYDataSubset.DataStartIndex = 0
					End If

					If intSmoothedYDataEndIndex >= intDataCount Then
						intSmoothedYDataEndIndex = intDataCount - 1
					End If

					' Copy the smoothed data into sngSmoothedYData
					With udtSmoothedYDataSubset
						.DataCount = intSmoothedYDataEndIndex - .DataStartIndex + 1

						If .DataCount > .Data.Length Then
							ReDim .Data(.DataCount - 1)
						Else
							Array.Clear(.Data, 0, .Data.Length)
						End If

						For intIndex = .DataStartIndex To intSmoothedYDataEndIndex
							.Data(intIndex - .DataStartIndex) = CSng(Math.Min(udtPeakData.SmoothedYData(intIndex), Single.MaxValue))
						Next intIndex

					End With

					' Copy the PeakLocs and PeakEdges into udtPeakDataSaved since we're going to call FindPeaksWork again and the data will get overwritten
					' We first equate udtPeakDataSaved to udtPeakData, effectively performing a shallow copy
					udtPeakDataSaved = udtPeakData
					With udtPeakDataSaved
						ReDim .PeakLocs(.PeakCount - 1)
						ReDim .PeakEdgesLeft(.PeakCount - 1)
						ReDim .PeakEdgesRight(.PeakCount - 1)
						ReDim .PeakAreas(.PeakCount - 1)
						ReDim .PeakIsValid(.PeakCount - 1)

						For intPeakIndexCompare = 0 To .PeakCount - 1
							.PeakLocs(intPeakIndexCompare) = udtPeakData.PeakLocs(intPeakIndexCompare)
							.PeakEdgesLeft(intPeakIndexCompare) = udtPeakData.PeakEdgesLeft(intPeakIndexCompare)
							.PeakEdgesRight(intPeakIndexCompare) = udtPeakData.PeakEdgesRight(intPeakIndexCompare)
							.PeakAreas(intPeakIndexCompare) = udtPeakData.PeakAreas(intPeakIndexCompare)
							.PeakIsValid(intPeakIndexCompare) = udtPeakData.PeakIsValid(intPeakIndexCompare)
						Next intPeakIndexCompare
					End With

					If udtPeakData.PeakWidthPointsMinimum <> MINIMUM_PEAK_WIDTH Then
						' Determine the number of shoulder peaks for this peak
						' Use a minimum peak width of MINIMUM_PEAK_WIDTH and use a Max Allow Upward Spike Fraction of just 0.05 (= 5%)
						udtPeakData.PeakWidthPointsMinimum = MINIMUM_PEAK_WIDTH
						If udtPeakData.MaxAllowedUpwardSpikeFractionMax > 0.05 Then
							udtPeakData.MaxAllowedUpwardSpikeFractionMax = 0.05
						End If
						blnValidPeakFound = FindPeaksWork(objPeakDetector, intScanNumbers, udtPeakData, blnSIMDataPresent, udtSICPeakFinderOptions, True, blnReturnClosestPeak)

						If blnValidPeakFound Then
							With udtPeakData
								intShoulderCount = 0
								For intIndex = 0 To .PeakCount - 1
									If .PeakLocs(intIndex) >= intPeakIndexStart AndAlso .PeakLocs(intIndex) <= intPeakIndexEnd Then
										' The peak at intIndex has a peak center between the "official" peak's boundaries
										' Make sure it's not the same peak as the "official" peak
										If .PeakLocs(intIndex) <> intPeakLocationIndex Then
											' Now see if the comparison peak's intensity is at least .IntensityThresholdFractionMax of the intensity of the "official" peak
											If sngIntensityData(.PeakLocs(intIndex)) >= udtSICPeakFinderOptions.IntensityThresholdFractionMax * sngIntensityData(intPeakLocationIndex) Then
												' Yes, this is a shoulder peak
												intShoulderCount += 1
											End If
										End If
									End If
								Next intIndex
							End With
						End If
					Else
						intShoulderCount = 0
					End If

					' Make sure intPeakLocationIndex really is the point with the highest intensity (in the smoothed data)
					dblMaximumIntensity = udtPeakData.SmoothedYData(intPeakLocationIndex)
					For intIndex = intPeakIndexStart To intPeakIndexEnd
						If udtPeakData.SmoothedYData(intIndex) > dblMaximumIntensity Then
							' A more intense data point was found; update intPeakLocationIndex
							dblMaximumIntensity = udtPeakData.SmoothedYData(intIndex)
							intPeakLocationIndex = intIndex
						End If
					Next intIndex


					Dim intComparisonPeakEdgeIndex As Integer
					Dim sngTargetIntensity As Single
					Dim intDataIndex As Integer

					' Populate intPreviousPeakFWHMPointRight and intNextPeakFWHMPointLeft
					sngAdjacentPeakIntensityThreshold = sngIntensityData(intPeakLocationIndex) / 3

					' Search through udtPeakDataSaved to find the closest peak (with a signficant intensity) to the left of this peak
					' Note that the peaks in udtPeakDataSaved are not necessarily ordered by increasing index, 
					'  thus the need for an exhaustive search
					intAdjacentIndex = -1		' Initially assign an invalid index
					intSmallestIndexDifference = intDataCount + 1
					For intPeakIndexCompare = 0 To udtPeakDataSaved.PeakCount - 1
						If intPeakIndexCompare <> udtPeakDataSaved.BestPeakIndex AndAlso _
						   udtPeakDataSaved.PeakLocs(intPeakIndexCompare) <= intPeakIndexStart Then
							' The peak is before intPeakIndexStart; is its intensity large enough?
							If sngIntensityData(udtPeakDataSaved.PeakLocs(intPeakIndexCompare)) >= sngAdjacentPeakIntensityThreshold Then
								' Yes, the intensity is large enough

								' Initialize intComparisonPeakedgeIndex to the right edge of the adjacent peak
								intComparisonPeakEdgeIndex = udtPeakDataSaved.PeakEdgesRight(intPeakIndexCompare)

								' Find the first point in the adjacent peak that is at least 50% of the maximum in the adjacent peak
								' Store that point in intComparisonPeakedgeIndex
								sngTargetIntensity = sngIntensityData(udtPeakDataSaved.PeakLocs(intPeakIndexCompare)) / 2
								For intDataIndex = intComparisonPeakEdgeIndex To udtPeakDataSaved.PeakLocs(intPeakIndexCompare) Step -1
									If sngIntensityData(intDataIndex) >= sngTargetIntensity Then
										intComparisonPeakEdgeIndex = intDataIndex
										Exit For
									End If
								Next intDataIndex

								' Assure that intComparisonPeakEdgeIndex is less than intPeakIndexStart
								If intComparisonPeakEdgeIndex >= intPeakIndexStart Then
									intComparisonPeakEdgeIndex = intPeakIndexStart - 1
									If intComparisonPeakEdgeIndex < 0 Then intComparisonPeakEdgeIndex = 0
								End If

								' Possibly update intPreviousPeakFWHMPointRight
								If intPeakIndexStart - intComparisonPeakEdgeIndex <= intSmallestIndexDifference Then
									intPreviousPeakFWHMPointRight = intComparisonPeakEdgeIndex
									intSmallestIndexDifference = intPeakIndexStart - intComparisonPeakEdgeIndex
									intAdjacentIndex = intPeakIndexCompare
								End If
							End If
						End If
					Next intPeakIndexCompare

					' Search through udtPeakDataSaved to find the closest peak to the right of this peak
					intAdjacentIndex = udtPeakDataSaved.PeakCount	 ' Initially assign an invalid index
					intSmallestIndexDifference = intDataCount + 1
					For intPeakIndexCompare = udtPeakDataSaved.PeakCount - 1 To 0 Step -1
						If intPeakIndexCompare <> udtPeakDataSaved.BestPeakIndex AndAlso _
						   udtPeakDataSaved.PeakLocs(intPeakIndexCompare) >= intPeakIndexEnd Then

							' The peak is after intPeakIndexEnd; is its intensity large enough?
							If sngIntensityData(udtPeakDataSaved.PeakLocs(intPeakIndexCompare)) >= sngAdjacentPeakIntensityThreshold Then
								' Yes, the intensity is large enough

								' Initialize intComparisonPeakEdgeIndex to the left edge of the adjacent peak
								intComparisonPeakEdgeIndex = udtPeakDataSaved.PeakEdgesLeft(intPeakIndexCompare)

								' Find the first point in the adjacent peak that is at least 50% of the maximum in the adjacent peak
								' Store that point in intComparisonPeakedgeIndex
								sngTargetIntensity = sngIntensityData(udtPeakDataSaved.PeakLocs(intPeakIndexCompare)) / 2
								For intDataIndex = intComparisonPeakEdgeIndex To udtPeakDataSaved.PeakLocs(intPeakIndexCompare)
									If sngIntensityData(intDataIndex) >= sngTargetIntensity Then
										intComparisonPeakEdgeIndex = intDataIndex
										Exit For
									End If
								Next intDataIndex

								' Assure that intComparisonPeakEdgeIndex is greater than intPeakIndexEnd
								If intPeakIndexEnd >= intComparisonPeakEdgeIndex Then
									intComparisonPeakEdgeIndex = intPeakIndexEnd + 1
									If intComparisonPeakEdgeIndex >= intDataCount Then intComparisonPeakEdgeIndex = intDataCount - 1
								End If

								' Possibly update intNextPeakFWHMPointLeft
								If intComparisonPeakEdgeIndex - intPeakIndexEnd <= intSmallestIndexDifference Then
									intNextPeakFWHMPointLeft = intComparisonPeakEdgeIndex
									intSmallestIndexDifference = intComparisonPeakEdgeIndex - intPeakIndexEnd
									intAdjacentIndex = intPeakIndexCompare
								End If
							End If
						End If
					Next intPeakIndexCompare


				Else
					' No peaks or no peaks containing .OriginalPeakLocationIndex
					' If udtPeakData.PeakWidthPointsMinimum is greater than 3 and blnTestingMinimumPeakWidth = False, then decrement it by 50%
					If udtPeakData.PeakWidthPointsMinimum > MINIMUM_PEAK_WIDTH AndAlso Not blnTestingMinimumPeakWidth Then
						udtPeakData.PeakWidthPointsMinimum = CInt(Math.Floor(udtPeakData.PeakWidthPointsMinimum / 2))
						If udtPeakData.PeakWidthPointsMinimum < MINIMUM_PEAK_WIDTH Then udtPeakData.PeakWidthPointsMinimum = MINIMUM_PEAK_WIDTH
					Else
						intPeakLocationIndex = udtPeakData.OriginalPeakLocationIndex
						intPeakIndexStart = udtPeakData.OriginalPeakLocationIndex
						intPeakIndexEnd = udtPeakData.OriginalPeakLocationIndex
						intPreviousPeakFWHMPointRight = intPeakIndexStart
						intNextPeakFWHMPointLeft = intPeakIndexEnd
						blnValidPeakFound = True
					End If
				End If
			Loop While Not blnValidPeakFound

		Catch ex As Exception
			LogErrors("clsMASICPeakFinder->FindPeaks", "Error in FindPeaks", ex, True, False, True)
			blnValidPeakFound = False
		End Try

		Return blnValidPeakFound

	End Function

	Private Function FindPeaksWork(ByVal objPeakDetector As clsPeakDetection, ByVal intScanNumbers() As Integer, ByRef udtPeakData As udtFindPeaksDataType, ByVal blnSIMDataPresent As Boolean, ByVal udtSICPeakFinderOptions As udtSICPeakFinderOptionsType, ByVal blnTestingMinimumPeakWidth As Boolean, ByVal blnReturnClosestPeak As Boolean) As Boolean
		' Returns True if a valid peak is found; otherwise, returns false
		' When blnReturnClosestPeak is True, then a valid peak is one that contains udtPeakData.OriginalPeakLocationIndex
		' When blnReturnClosestPeak is False, then stores the index of the most intense peak in udtpeakdata.BestPeakIndex
		' All of the identified peaks are returned in udtpeakdata.PeakLocs(), regardless of whether they are valid or not

		Dim intFoundPeakIndex As Integer

		Dim sngPeakMaximum As Single

		Dim blnDataIsSmoothed As Boolean

		' ReSharper disable once NotAccessedVariable
		Dim blnUsedSmoothedDataForPeakDetection As Boolean

		Dim blnValidPeakFound As Boolean
		Dim blnSuccess As Boolean

		Dim intPeakLocationIndex As Integer
		Dim intPeakIndexStart, intPeakIndexEnd As Integer

		Dim intStepOverIncreaseCount As Integer

		Dim strErrorMessage As String = String.Empty

		' Smooth the Y data, and store in udtPeakData.SmoothedYData
		' Note that if using a Butterworth filter, then we increase udtPeakData.PeakWidthPointsMinimum if too small, compared to 1/SamplingFrequency
		blnDataIsSmoothed = FindPeaksWorkSmoothData(udtPeakData, blnSIMDataPresent, udtSICPeakFinderOptions, udtPeakData.PeakWidthPointsMinimum, strErrorMessage)
		If udtSICPeakFinderOptions.FindPeaksOnSmoothedData AndAlso blnDataIsSmoothed Then
			udtPeakData.PeakCount = objPeakDetector.DetectPeaks(udtPeakData.XData, udtPeakData.SmoothedYData, udtSICPeakFinderOptions.IntensityThresholdAbsoluteMinimum, udtPeakData.PeakWidthPointsMinimum, udtPeakData.PeakLocs, udtPeakData.PeakEdgesLeft, udtPeakData.PeakEdgesRight, udtPeakData.PeakAreas, CInt(udtSICPeakFinderOptions.IntensityThresholdFractionMax * 100), 2, True, True)
			blnUsedSmoothedDataForPeakDetection = True
		Else
			' Look for the peaks, using udtPeakData.PeakWidthPointsMinimum as the minimum peak width 
			udtPeakData.PeakCount = objPeakDetector.DetectPeaks(udtPeakData.XData, udtPeakData.YData, udtSICPeakFinderOptions.IntensityThresholdAbsoluteMinimum, udtPeakData.PeakWidthPointsMinimum, udtPeakData.PeakLocs, udtPeakData.PeakEdgesLeft, udtPeakData.PeakEdgesRight, udtPeakData.PeakAreas, CInt(udtSICPeakFinderOptions.IntensityThresholdFractionMax * 100), 2, True, True)
			blnUsedSmoothedDataForPeakDetection = False
		End If


		If udtPeakData.PeakCount = -1 Then
			' Fatal error occurred while finding peaks
			Return False
		End If

		If blnTestingMinimumPeakWidth Then
			If udtPeakData.PeakCount <= 0 Then
				' No peaks were found; create a new peak list using the original peak location index as the peak center
				With udtPeakData
					.PeakCount = 1
					ReDim .PeakLocs(0)
					ReDim .PeakEdgesLeft(0)
					ReDim .PeakEdgesRight(0)

					.PeakLocs(0) = udtPeakData.OriginalPeakLocationIndex
					.PeakEdgesLeft(0) = udtPeakData.OriginalPeakLocationIndex
					.PeakEdgesRight(0) = udtPeakData.OriginalPeakLocationIndex
				End With
			Else
				If blnReturnClosestPeak Then
					' Make sure one of the peaks is within 1 of the original peak location 
					blnSuccess = False
					For intFoundPeakIndex = 0 To udtPeakData.PeakCount - 1
						If Math.Abs(udtPeakData.PeakLocs(intFoundPeakIndex) - udtPeakData.OriginalPeakLocationIndex) <= 1 Then
							blnSuccess = True
							Exit For
						End If
					Next intFoundPeakIndex

					If Not blnSuccess Then
						' No match was found; add a new peak at udtPeakData.OriginalPeakLocationIndex
						With udtPeakData
							ReDim Preserve .PeakLocs(udtPeakData.PeakCount)
							ReDim Preserve .PeakEdgesLeft(udtPeakData.PeakCount)
							ReDim Preserve .PeakEdgesRight(udtPeakData.PeakCount)
							ReDim Preserve .PeakAreas(udtPeakData.PeakCount)

							.PeakLocs(udtPeakData.PeakCount) = .OriginalPeakLocationIndex
							.PeakEdgesLeft(udtPeakData.PeakCount) = .OriginalPeakLocationIndex
							.PeakEdgesRight(udtPeakData.PeakCount) = .OriginalPeakLocationIndex
							.PeakAreas(udtPeakData.PeakCount) = .YData(udtPeakData.OriginalPeakLocationIndex)

							.PeakCount += 1
						End With
					End If
				End If
			End If
		End If

		If udtPeakData.PeakCount <= 0 Then
			' No peaks were found
			blnValidPeakFound = False
		Else

			ReDim udtPeakData.PeakIsValid(udtPeakData.PeakCount - 1)
			For intFoundPeakIndex = 0 To udtPeakData.PeakCount - 1

				' Find the center and boundaries of this peak

				' Copy from the PeakEdges arrays to the working variables
				intPeakLocationIndex = udtPeakData.PeakLocs(intFoundPeakIndex)
				intPeakIndexStart = udtPeakData.PeakEdgesLeft(intFoundPeakIndex)
				intPeakIndexEnd = udtPeakData.PeakEdgesRight(intFoundPeakIndex)

				' Make sure intPeakLocationIndex is between intPeakIndexStart and intPeakIndexEnd
				If intPeakIndexStart > intPeakLocationIndex Then
					LogErrors("clsMasicPeakFinder->FindPeaksWork", "intPeakIndexStart is > intPeakLocationIndex; this is probably a programming error", Nothing, True, False)
					intPeakIndexStart = intPeakLocationIndex
				End If

				If intPeakIndexEnd < intPeakLocationIndex Then
					LogErrors("clsMasicPeakFinder->FindPeaksWork", "intPeakIndexEnd is < intPeakLocationIndex; this is probably a programming error", Nothing, True, False)
					intPeakIndexEnd = intPeakLocationIndex
				End If

				' See if the peak boundaries (left and right edges) need to be narrowed or expanded 
				' Do this by stepping left or right while the intensity is decreasing.  If an increase is found, but the 
				' next point after the increasing point is less than the current point, then possibly keep stepping; the 
				' test for whether to keep stepping is that the next point away from the increasing point must be less 
				' than the current point.  If this is the case, replace the increasing point with the average of the 
				' current point and the point two points away
				'
				' Use smoothed data for this step
				' Determine the smoothing window based on udtPeakData.PeakWidthPointsMinimum
				' If udtPeakData.PeakWidthPointsMinimum <= 4 then do not filter

				If Not blnDataIsSmoothed Then
					' Need to smooth the data now
					blnDataIsSmoothed = FindPeaksWorkSmoothData(udtPeakData, blnSIMDataPresent, udtSICPeakFinderOptions, udtPeakData.PeakWidthPointsMinimum, strErrorMessage)
				End If

				' First see if we need to narrow the peak by looking for decreasing intensities moving toward the peak center
				' We'll use the unsmoothed data for this
				Do While intPeakIndexStart < intPeakLocationIndex - 1
					If udtPeakData.YData(intPeakIndexStart) > udtPeakData.YData(intPeakIndexStart + 1) Then
						' OrElse (blnUsedSmoothedDataForPeakDetection AndAlso udtPeakData.SmoothedYData(intPeakIndexStart) < 0) Then
						intPeakIndexStart += 1
					Else
						Exit Do
					End If
				Loop

				Do While intPeakIndexEnd > intPeakLocationIndex + 1
					If udtPeakData.YData(intPeakIndexEnd - 1) < udtPeakData.YData(intPeakIndexEnd) Then
						' OrElse (blnUsedSmoothedDataForPeakDetection AndAlso udtPeakData.SmoothedYData(intPeakIndexEnd) < 0) Then
						intPeakIndexEnd -= 1
					Else
						Exit Do
					End If
				Loop


				' Now see if we need to expand the peak by looking for decreasing intensities moving away from the peak center, 
				'  but allowing for small increases
				' We'll use the smoothed data for this; if we encounter negative values in the smoothed data, we'll keep going until we reach the low point since huge peaks can cause some odd behavior with the Butterworth filter
				' Keep track of the number of times we step over an increased value
				intStepOverIncreaseCount = 0
				Do While intPeakIndexStart > 0
					'dblCurrentSlope = objPeakDetector.ComputeSlope(udtPeakData.XData, udtPeakData.SmoothedYData, intPeakIndexStart, intPeakLocationIndex)

					'If dblCurrentSlope > 0 AndAlso _
					'   intPeakLocationIndex - intPeakIndexStart > 3 AndAlso _
					'   udtPeakData.SmoothedYData(intPeakIndexStart - 1) < Math.Max(udtSICPeakFinderOptions.IntensityThresholdFractionMax * sngPeakMaximum, udtSICPeakFinderOptions.IntensityThresholdAbsoluteMinimum) Then
					'    ' We reached a low intensity data point and we're going downhill (i.e. the slope from this point to intPeakLocationIndex is positive)
					'    ' Step once more and stop
					'    intPeakIndexStart -= 1
					'    Exit Do
					If udtPeakData.SmoothedYData(intPeakIndexStart - 1) < udtPeakData.SmoothedYData(intPeakIndexStart) Then
						' The adjacent point is lower than the current point
						intPeakIndexStart -= 1
					ElseIf Math.Abs(udtPeakData.SmoothedYData(intPeakIndexStart - 1) - udtPeakData.SmoothedYData(intPeakIndexStart)) < Double.Epsilon Then
						' The adjacent point is equal to the current point
						intPeakIndexStart -= 1
					Else
						' The next point to the left is not lower; what about the point after it?
						If intPeakIndexStart > 1 Then
							If udtPeakData.SmoothedYData(intPeakIndexStart - 2) <= udtPeakData.SmoothedYData(intPeakIndexStart) Then
								' Only allow ignoring an upward spike if the delta from this point to the next is <= .MaxAllowedUpwardSpikeFractionMax of sngPeakMaximum
								If udtPeakData.SmoothedYData(intPeakIndexStart - 1) - udtPeakData.SmoothedYData(intPeakIndexStart) > udtPeakData.MaxAllowedUpwardSpikeFractionMax * sngPeakMaximum Then
									Exit Do
								End If

								If blnDataIsSmoothed Then
									' Only ignore an upward spike twice if the data is smoothed
									If intStepOverIncreaseCount >= 2 Then Exit Do
								End If

								intPeakIndexStart -= 1

								intStepOverIncreaseCount += 1
							Else
								Exit Do
							End If
						Else
							Exit Do
						End If
					End If
				Loop

				intStepOverIncreaseCount = 0
				Do While intPeakIndexEnd < udtPeakData.SourceDataCount - 1
					'dblCurrentSlope = objPeakDetector.ComputeSlope(udtPeakData.XData, udtPeakData.SmoothedYData, intPeakLocationIndex, intPeakIndexEnd)

					'If dblCurrentSlope < 0 AndAlso _
					'   intPeakIndexEnd - intPeakLocationIndex > 3 AndAlso _
					'   udtPeakData.SmoothedYData(intPeakIndexEnd + 1) < Math.Max(udtSICPeakFinderOptions.IntensityThresholdFractionMax * sngPeakMaximum, udtSICPeakFinderOptions.IntensityThresholdAbsoluteMinimum) Then
					'    ' We reached a low intensity data point and we're going downhill (i.e. the slope from intPeakLocationIndex to this point is negative)
					'    intPeakIndexEnd += 1
					'    Exit Do
					If udtPeakData.SmoothedYData(intPeakIndexEnd + 1) < udtPeakData.SmoothedYData(intPeakIndexEnd) Then
						' The adjacent point is lower than the current point
						intPeakIndexEnd += 1
					ElseIf Math.Abs(udtPeakData.SmoothedYData(intPeakIndexEnd + 1) - udtPeakData.SmoothedYData(intPeakIndexEnd)) < Double.Epsilon Then
						' The adjacent point is equal to the current point
						intPeakIndexEnd += 1
					Else
						' The next point to the right is not lower; what about the point after it?
						If intPeakIndexEnd < udtPeakData.SourceDataCount - 2 Then
							If udtPeakData.SmoothedYData(intPeakIndexEnd + 2) <= udtPeakData.SmoothedYData(intPeakIndexEnd) Then
								' Only allow ignoring an upward spike if the delta from this point to the next is <= .MaxAllowedUpwardSpikeFractionMax of sngPeakMaximum
								If udtPeakData.SmoothedYData(intPeakIndexEnd + 1) - udtPeakData.SmoothedYData(intPeakIndexEnd) > udtPeakData.MaxAllowedUpwardSpikeFractionMax * sngPeakMaximum Then
									Exit Do
								End If

								If blnDataIsSmoothed Then
									' Only ignore an upward spike twice if the data is smoothed
									If intStepOverIncreaseCount >= 2 Then Exit Do
								End If

								intPeakIndexEnd += 1

								intStepOverIncreaseCount += 1
							Else
								Exit Do
							End If
						Else
							Exit Do
						End If
					End If
				Loop

				udtPeakData.PeakIsValid(intFoundPeakIndex) = True
				If blnReturnClosestPeak Then
					' If udtPeakData.OriginalPeakLocationIndex is not between intPeakIndexStart and intPeakIndexEnd, then check
					'  if the scan number for udtPeakData.OriginalPeakLocationIndex is within .MaxDistanceScansNoOverlap scans of
					'  either of the peak edges; if not, then mark the peak as invalid since it does not contain the 
					'  scan for the parent ion
					If udtPeakData.OriginalPeakLocationIndex < intPeakIndexStart Then
						If Math.Abs(intScanNumbers(udtPeakData.OriginalPeakLocationIndex) - intScanNumbers(intPeakIndexStart)) > udtSICPeakFinderOptions.MaxDistanceScansNoOverlap Then
							udtPeakData.PeakIsValid(intFoundPeakIndex) = False
						End If
					ElseIf udtPeakData.OriginalPeakLocationIndex > intPeakIndexEnd Then
						If Math.Abs(intScanNumbers(udtPeakData.OriginalPeakLocationIndex) - intScanNumbers(intPeakIndexEnd)) > udtSICPeakFinderOptions.MaxDistanceScansNoOverlap Then
							udtPeakData.PeakIsValid(intFoundPeakIndex) = False
						End If
					End If
				End If

				' Copy back from the working variables to the PeakEdges arrays
				udtPeakData.PeakLocs(intFoundPeakIndex) = intPeakLocationIndex
				udtPeakData.PeakEdgesLeft(intFoundPeakIndex) = intPeakIndexStart
				udtPeakData.PeakEdgesRight(intFoundPeakIndex) = intPeakIndexEnd

			Next intFoundPeakIndex

			' Find the peak with the largest area that has udtPeakData.PeakIsValid = True
			udtPeakData.BestPeakIndex = -1
			udtPeakData.BestPeakArea = Single.MinValue
			For intFoundPeakIndex = 0 To udtPeakData.PeakCount - 1
				If udtPeakData.PeakIsValid(intFoundPeakIndex) Then
					If udtPeakData.PeakAreas(intFoundPeakIndex) > udtPeakData.BestPeakArea Then
						udtPeakData.BestPeakIndex = intFoundPeakIndex
						udtPeakData.BestPeakArea = CSng(Math.Min(udtPeakData.PeakAreas(intFoundPeakIndex), Single.MaxValue))
					End If
				End If
			Next intFoundPeakIndex

			If udtPeakData.BestPeakIndex >= 0 Then
				blnValidPeakFound = True
			Else
				blnValidPeakFound = False
			End If
		End If

		Return blnValidPeakFound

	End Function

	Private Function FindPeaksWorkSmoothData(ByRef udtPeakData As udtFindPeaksDataType, ByVal blnSIMDataPresent As Boolean, ByRef udtSICPeakFinderOptions As udtSICPeakFinderOptionsType, ByRef intPeakWidthPointsMinimum As Integer, ByRef strErrorMessage As String) As Boolean
		' Returns True if the data was smoothed; false if not or an error
		' The smoothed data is returned in udtPeakData.SmoothedYData

		Dim intFilterThirdWidth As Integer
		Dim blnSuccess As Boolean

		Dim sngButterWorthFrequency As Single

		Dim intPeakWidthPointsCompare As Integer

		Dim objFilter As New DataFilter.clsDataFilter

		ReDim udtPeakData.SmoothedYData(udtPeakData.SourceDataCount - 1)

		If (intPeakWidthPointsMinimum > 4 AndAlso (udtSICPeakFinderOptions.UseSavitzkyGolaySmooth OrElse udtSICPeakFinderOptions.UseButterworthSmooth)) OrElse _
		 udtSICPeakFinderOptions.SmoothDataRegardlessOfMinimumPeakWidth Then

			udtPeakData.YData.CopyTo(udtPeakData.SmoothedYData, 0)

			If udtSICPeakFinderOptions.UseButterworthSmooth Then
				' Filter the data with a Butterworth filter (.UseButterworthSmooth takes precedence over .UseSavitzkyGolaySmooth)
				If blnSIMDataPresent AndAlso udtSICPeakFinderOptions.ButterworthSamplingFrequencyDoubledForSIMData Then
					sngButterWorthFrequency = udtSICPeakFinderOptions.ButterworthSamplingFrequency * 2
				Else
					sngButterWorthFrequency = udtSICPeakFinderOptions.ButterworthSamplingFrequency
				End If
				blnSuccess = objFilter.ButterworthFilter(udtPeakData.SmoothedYData, 0, udtPeakData.SourceDataCount - 1, sngButterWorthFrequency)
				If Not blnSuccess Then
					LogErrors("clsMasicPeakFinder->FindPeaksWorkSmoothData", "Error with the Butterworth filter" & strErrorMessage, Nothing, True, False)
					Return False
				Else
					' Data was smoothed
					' Validate that intPeakWidthPointsMinimum is large enough
					If sngButterWorthFrequency > 0 Then
						intPeakWidthPointsCompare = CInt(Math.Round(1 / sngButterWorthFrequency, 0))
						If intPeakWidthPointsMinimum < intPeakWidthPointsCompare Then
							intPeakWidthPointsMinimum = intPeakWidthPointsCompare
						End If
					End If

					Return True
				End If

			Else
				' Filter the data with a Savitzky Golay filter
				intFilterThirdWidth = CInt(Math.Floor(udtPeakData.PeakWidthPointsMinimum / 3))
				If intFilterThirdWidth > 3 Then intFilterThirdWidth = 3

				' Make sure intFilterThirdWidth is Odd
				If intFilterThirdWidth Mod 2 = 0 Then
					intFilterThirdWidth -= 1
				End If

				' Note that the SavitzkyGolayFilter doesn't work right for PolynomialDegree values greater than 0
				' Also note that a PolynomialDegree value of 0 results in the equivalent of a moving average filter
				blnSuccess = objFilter.SavitzkyGolayFilter(udtPeakData.SmoothedYData, 0, udtPeakData.SmoothedYData.Length - 1, intFilterThirdWidth, intFilterThirdWidth, udtSICPeakFinderOptions.SavitzkyGolayFilterOrder, True, strErrorMessage)
				If Not blnSuccess Then
					LogErrors("clsMasicPeakFinder->FindPeaksWorkSmoothData", "Error with the Savitzky-Golay filter: " & strErrorMessage, Nothing, True, False)
					Return False
				Else
					' Data was smoothed
					Return True
				End If
			End If
		Else
			' Do not filter
			udtPeakData.YData.CopyTo(udtPeakData.SmoothedYData, 0)
			Return False
		End If

	End Function

	Public Sub FindPotentialPeakArea(ByVal intSICDataCount As Integer, ByRef SICData() As Single, ByRef udtSICPotentialAreaStats As udtSICPotentialAreaStatsType, ByVal udtSICPeakFinderOptions As udtSICPeakFinderOptionsType)
		' This function computes the potential peak area for a given SIC 
		'  and stores in udtSICPotentialAreaStats.MinimumPotentialPeakArea
		' However, the summed intensity is not used if the number of points >= .SICBaselineNoiseOptions.MinimumBaselineNoiseLevel is less than Minimum_Peak_Width

		' Note: You cannot use SICData.Length to determine the length of the array; use intDataCount

		Dim intIndex As Integer

		Dim sngMinimumPositiveValue As Single
		Dim sngIntensityToUse As Single

		Dim dblOldestIntensity As Double
		Dim dblPotentialPeakArea, dblMinimumPotentialPeakArea As Double
		Dim intPeakCountBasisForMinimumPotentialArea As Integer
		Dim intValidPeakCount As Integer

		' The queue is used to keep track of the most recent intensity values
		Dim queIntensityList As New Queue

		dblMinimumPotentialPeakArea = Double.MaxValue
		intPeakCountBasisForMinimumPotentialArea = 0

		If intSICDataCount > 0 Then

			queIntensityList.Clear()
			dblPotentialPeakArea = 0
			intValidPeakCount = 0

			' Find the minimum intensity in SICData()
			sngMinimumPositiveValue = FindMinimumPositiveValue(intSICDataCount, SICData, 1)

			For intIndex = 0 To intSICDataCount - 1

				' If this data point is > .MinimumBaselineNoiseLevel, then add this intensity to dblPotentialPeakArea
				'  and increment intValidPeakCount
				sngIntensityToUse = Math.Max(sngMinimumPositiveValue, SICData(intIndex))
				If sngIntensityToUse >= udtSICPeakFinderOptions.SICBaselineNoiseOptions.MinimumBaselineNoiseLevel Then
					dblPotentialPeakArea += sngIntensityToUse
					intValidPeakCount += 1
				End If

				If queIntensityList.Count >= udtSICPeakFinderOptions.InitialPeakWidthScansMaximum Then
					' Decrement dblPotentialPeakArea by the oldest item in the queue
					' If that item is >= .MinimumBaselineNoiseLevel, then decrement intValidPeakCount too
					dblOldestIntensity = CDbl(queIntensityList.Dequeue())

					If dblOldestIntensity >= udtSICPeakFinderOptions.SICBaselineNoiseOptions.MinimumBaselineNoiseLevel AndAlso _
					   dblOldestIntensity > 0 Then
						dblPotentialPeakArea -= dblOldestIntensity
						intValidPeakCount -= 1
					End If
				End If
				' Add this intensity to the queue
				queIntensityList.Enqueue(sngIntensityToUse)

				If dblPotentialPeakArea > 0 AndAlso intValidPeakCount >= MINIMUM_PEAK_WIDTH Then
					If intValidPeakCount > intPeakCountBasisForMinimumPotentialArea Then
						' The non valid peak count value is larger than the one associated with the current
						'  minimum potential peak area; update the minimum peak area to dblPotentialPeakArea
						dblMinimumPotentialPeakArea = dblPotentialPeakArea
						intPeakCountBasisForMinimumPotentialArea = intValidPeakCount
					Else
						If dblPotentialPeakArea < dblMinimumPotentialPeakArea AndAlso intValidPeakCount = intPeakCountBasisForMinimumPotentialArea Then
							dblMinimumPotentialPeakArea = dblPotentialPeakArea
						End If
					End If
				End If
			Next intIndex

		End If

		If dblMinimumPotentialPeakArea >= Double.MaxValue Then
			dblMinimumPotentialPeakArea = 1
		End If

		With udtSICPotentialAreaStats
			.MinimumPotentialPeakArea = dblMinimumPotentialPeakArea
			.PeakCountBasisForMinimumPotentialArea = intPeakCountBasisForMinimumPotentialArea
		End With

	End Sub

	Public Function FindSICPeakAndArea(ByVal intSICDataCount As Integer, ByVal SICScanNumbers() As Integer, ByVal SICData() As Single, ByRef udtSICPotentialAreaStatsForPeak As udtSICPotentialAreaStatsType, ByRef udtSICPeak As udtSICStatsPeakType, ByRef udtSmoothedYDataSubset As udtSmoothedYDataSubsetType, ByVal udtSICPeakFinderOptions As udtSICPeakFinderOptionsType, ByRef udtSICPotentialAreaStatsForRegion As udtSICPotentialAreaStatsType, ByVal blnReturnClosestPeak As Boolean, ByVal blnSIMDataPresent As Boolean, ByVal blnRecomputeNoiseLevel As Boolean) As Boolean
		' Note: The calling function should populate udtSICPeak.IndexObserved with the index in SICData() that the 
		'       parent ion m/z was actually observed; this will be used as the default peak location if a peak cannot be found

		' Note: You cannot use SICScanNumbers().Length or SICData().Length to determine the length of the array; use intSICDataCount

		' Set blnSIMDataPresent to True when there are large gaps in the survey scan numbers

		Dim intDataIndex As Integer
		Dim sngIntensityCompare As Single

		Dim blnSuccess As Boolean

		Try
			' Compute the potential peak area for this SIC
			FindPotentialPeakArea(intSICDataCount, SICData, udtSICPotentialAreaStatsForPeak, udtSICPeakFinderOptions)

			' See if the potential peak area for this SIC is lower than the values for the Region
			' If so, then update the region values with this peak's values
			With udtSICPotentialAreaStatsForPeak
				If .MinimumPotentialPeakArea > 1 AndAlso .PeakCountBasisForMinimumPotentialArea >= MINIMUM_PEAK_WIDTH Then
					If .PeakCountBasisForMinimumPotentialArea > udtSICPotentialAreaStatsForRegion.PeakCountBasisForMinimumPotentialArea Then
						udtSICPotentialAreaStatsForRegion.MinimumPotentialPeakArea = .MinimumPotentialPeakArea
						udtSICPotentialAreaStatsForRegion.PeakCountBasisForMinimumPotentialArea = .PeakCountBasisForMinimumPotentialArea
					Else
						If .MinimumPotentialPeakArea < udtSICPotentialAreaStatsForRegion.MinimumPotentialPeakArea AndAlso _
						   .PeakCountBasisForMinimumPotentialArea >= udtSICPotentialAreaStatsForRegion.PeakCountBasisForMinimumPotentialArea Then
							udtSICPotentialAreaStatsForRegion.MinimumPotentialPeakArea = .MinimumPotentialPeakArea
							udtSICPotentialAreaStatsForRegion.PeakCountBasisForMinimumPotentialArea = .PeakCountBasisForMinimumPotentialArea
						End If
					End If
				End If
			End With

			If SICData Is Nothing OrElse SICData.Length = 0 OrElse intSICDataCount <= 0 Then
				' Either .SICData is nothing or no SIC data exists
				' Cannot find peaks for this parent ion
				With udtSICPeak
					.IndexObserved = 0
					.IndexBaseLeft = .IndexObserved
					.IndexBaseRight = .IndexObserved
					.IndexMax = .IndexObserved
					.ParentIonIntensity = 0
					.PreviousPeakFWHMPointRight = 0
					.NextPeakFWHMPointLeft = 0
				End With
			Else

				With udtSICPeak
					' Initialize the following to the entire range of the SICData
					.IndexBaseLeft = 0
					.IndexBaseRight = intSICDataCount - 1

					' Initialize .IndexMax to .IndexObserved (which should have been defined by the calling function)
					.IndexMax = .IndexObserved
					If .IndexMax < 0 OrElse .IndexMax >= intSICDataCount Then
						LogErrors("clsMasicPeakFinder->FindSICPeakAndArea", "Unexpected .IndexMax value", Nothing, True, False)
						.IndexMax = 0
					End If

					.PreviousPeakFWHMPointRight = .IndexBaseLeft
					.NextPeakFWHMPointLeft = .IndexBaseRight
				End With

				If blnRecomputeNoiseLevel Then
					' Compute the Noise Threshold for this SIC
					' This value is first computed using all data in the SIC; it is later updated 
					'  to be the minimum value of the average of the data to the immediate left and
					'  immediate right of the peak identified in the SIC
					blnSuccess = ComputeNoiseLevelForSICData(intSICDataCount, SICData, udtSICPeakFinderOptions.SICBaselineNoiseOptions, udtSICPeak.BaselineNoiseStats)
				End If


				' Use a peak-finder algorithm to find the peak closest to .Peak.IndexMax
				' Note that .Peak.IndexBaseLeft, .Peak.IndexBaseRight, and .Peak.IndexMax are passed ByRef and get updated by FindPeaks
				With udtSICPeak
					blnSuccess = FindPeaks(intSICDataCount, SICScanNumbers, SICData, .IndexBaseLeft, .IndexBaseRight, .IndexMax, _
					  .PreviousPeakFWHMPointRight, .NextPeakFWHMPointLeft, .ShoulderCount, _
					  udtSmoothedYDataSubset, blnSIMDataPresent, udtSICPeakFinderOptions, _
					  udtSICPeak.BaselineNoiseStats.NoiseLevel, _
					  udtSICPotentialAreaStatsForRegion.MinimumPotentialPeakArea, _
					  blnReturnClosestPeak)
				End With

				If blnSuccess Then
					' Update the maximum peak intensity (required prior to call to ComputeNoiseLevelInPeakVicinity and call to ComputeSICPeakArea)
					udtSICPeak.MaxIntensityValue = SICData(udtSICPeak.IndexMax)

					If blnRecomputeNoiseLevel Then
						' Update the value for udtSICPotentialAreaStatsForPeak.SICNoiseThresholdIntensity based on the data around the peak
						blnSuccess = ComputeNoiseLevelInPeakVicinity(intSICDataCount, SICScanNumbers, SICData, udtSICPeak, udtSICPeakFinderOptions.SICBaselineNoiseOptions)
					End If

					'' ' Compute the trimmed median of the data in SICData (replacing nonpositive values with the minimum)
					'' ' If the median is less than udtSICPeak.BaselineNoiseStats.NoiseLevel then update udtSICPeak.BaselineNoiseStats.NoiseLevel
					''udtNoiseOptionsOverride = udtSICPeakFinderOptions.SICBaselineNoiseOptions
					''With udtNoiseOptionsOverride
					''    .BaselineNoiseMode = eNoiseThresholdModes.TrimmedMedianByAbundance
					''    .TrimmedMeanFractionLowIntensityDataToAverage = 0.75
					''End With
					''blnSuccess = ComputeNoiseLevelForSICData(intSICDataCount, SICData, udtNoiseOptionsOverride, udtNoiseStatsCompare)
					''With udtNoiseStatsCompare
					''    If .PointsUsed >= MINIMUM_NOISE_SCANS_REQUIRED Then
					''        ' Check whether the comparison noise level is less than the existing noise level times 0.75
					''        If .NoiseLevel < udtSICPeak.BaselineNoiseStats.NoiseLevel * 0.75 Then
					''            ' Yes, the comparison noise level is lower
					''            ' Use a T-Test to see if the comparison noise level is significantly different than the primary noise level
					''            If TestSignificanceUsingTTest(.NoiseLevel, udtSICPeak.BaselineNoiseStats.NoiseLevel, .NoiseStDev, udtSICPeak.BaselineNoiseStats.NoiseStDev, .PointsUsed, udtSICPeak.BaselineNoiseStats.PointsUsed, eTTestConfidenceLevelConstants.Conf95Pct, dblTCalculated) Then
					''                udtSICPeak.BaselineNoiseStats = udtNoiseStatsCompare
					''            End If
					''        End If
					''    End If
					''End With

					' If smoothing was enabled, then see if the smoothed value is larger than udtSICPeak.MaxIntensityValue 
					' If it is, then use the smoothed value for udtSICPeak.MaxIntensityValue
					If udtSICPeakFinderOptions.UseSavitzkyGolaySmooth OrElse udtSICPeakFinderOptions.UseButterworthSmooth Then
						intDataIndex = udtSICPeak.IndexMax - udtSmoothedYDataSubset.DataStartIndex
						If intDataIndex >= 0 AndAlso Not udtSmoothedYDataSubset.Data Is Nothing AndAlso intDataIndex < udtSmoothedYDataSubset.DataCount Then
							' Possibly use the intensity of the smoothed data as the peak intensity
							sngIntensityCompare = udtSmoothedYDataSubset.Data(intDataIndex)
							If sngIntensityCompare > udtSICPeak.MaxIntensityValue Then
								udtSICPeak.MaxIntensityValue = sngIntensityCompare
							End If
						End If
					End If

					' Compute the signal to noise ratio for the peak
					With udtSICPeak
						.SignalToNoiseRatio = ComputeSignalToNoise(.MaxIntensityValue, .BaselineNoiseStats.NoiseLevel)
					End With

					' Compute the Full Width at Half Max (FWHM) value, this time subtracting the noise level from the baseline
					udtSICPeak.FWHMScanWidth = ComputeFWHM(SICScanNumbers, SICData, udtSICPeak, True)

					' Compute the Area (this function uses .FWHMScanWidth and therefore needs to be called after ComputeFWHM)
					blnSuccess = ComputeSICPeakArea(SICScanNumbers, SICData, udtSICPeak)

					' Compute the Statistical Moments values
					ComputeStatisticalMomentsStats(intSICDataCount, SICScanNumbers, SICData, udtSmoothedYDataSubset, udtSICPeak)

				Else
					' No peak found
					With udtSICPeak
						.MaxIntensityValue = SICData(udtSICPeak.IndexMax)
						.IndexBaseLeft = .IndexMax
						.IndexBaseRight = .IndexMax
						.FWHMScanWidth = 1

						' Assign the intensity of the peak at the observed maximum to the area
						.Area = .MaxIntensityValue

						.SignalToNoiseRatio = ComputeSignalToNoise(.MaxIntensityValue, .BaselineNoiseStats.NoiseLevel)
					End With
				End If

			End If


			blnSuccess = True

		Catch ex As Exception
			LogErrors("clsMASICPeakFinder->FindSICPeakAndArea", "Error finding SIC peaks and their areas", ex, True, False, True)
			blnSuccess = False
		End Try

		Return blnSuccess

	End Function

    Public Shared Function GetDefaultNoiseThresholdOptions() As udtBaselineNoiseOptionsType
        Dim udtBaselineNoiseOptions As udtBaselineNoiseOptionsType

        With udtBaselineNoiseOptions
            .BaselineNoiseMode = eNoiseThresholdModes.TrimmedMedianByAbundance
            .BaselineNoiseLevelAbsolute = 0
            .MinimumSignalToNoiseRatio = 0                      ' ToDo: Figure out how best to use this when > 0; for now, the SICNoiseMinimumSignalToNoiseRatio property ignores any attempts to set this value
            .MinimumBaselineNoiseLevel = 1
            .TrimmedMeanFractionLowIntensityDataToAverage = 0.75
            .DualTrimmedMeanStdDevLimits = 5
            .DualTrimmedMeanMaximumSegments = 3
        End With

        Return udtBaselineNoiseOptions
    End Function

    Public Shared Function GetDefaultSICPeakFinderOptions() As udtSICPeakFinderOptionsType
        Dim udtSICPeakFinderOptions As udtSICPeakFinderOptionsType

        With udtSICPeakFinderOptions
            .IntensityThresholdFractionMax = 0.01           ' 1% of the peak maximum
            .IntensityThresholdAbsoluteMinimum = 0

            ' Set the default SIC Baseline noise threshold options
            .SICBaselineNoiseOptions = GetDefaultNoiseThresholdOptions()
            ' Customize a few values
            With .SICBaselineNoiseOptions
                .BaselineNoiseMode = eNoiseThresholdModes.TrimmedMedianByAbundance
            End With

            .MaxDistanceScansNoOverlap = 0
            .MaxAllowedUpwardSpikeFractionMax = 0.2         ' 20%
            .InitialPeakWidthScansScaler = 1
            .InitialPeakWidthScansMaximum = 30

            .FindPeaksOnSmoothedData = True
            .SmoothDataRegardlessOfMinimumPeakWidth = True
            .UseButterworthSmooth = True                                ' If this is true, will ignore UseSavitzkyGolaySmooth
            .ButterworthSamplingFrequency = 0.25
            .ButterworthSamplingFrequencyDoubledForSIMData = True

            .UseSavitzkyGolaySmooth = True
            .SavitzkyGolayFilterOrder = 0                               ' Moving average filter if 0, Savitzky Golay filter if 2, 4, 6, etc.

            ' Set the default Mass Spectra noise threshold options
            .MassSpectraNoiseThresholdOptions = GetDefaultNoiseThresholdOptions()
            ' Customize a few values
            With .MassSpectraNoiseThresholdOptions
                .BaselineNoiseMode = eNoiseThresholdModes.TrimmedMedianByAbundance
                .TrimmedMeanFractionLowIntensityDataToAverage = 0.5
                .MinimumSignalToNoiseRatio = 2
            End With
        End With

        Return udtSICPeakFinderOptions

    End Function


    Private Function GetVersionForExecutingAssembly() As String
        Dim strVersion As String

        Try
			strVersion = Reflection.Assembly.GetExecutingAssembly.GetName.Version.ToString()
        Catch ex As Exception
            strVersion = "??.??.??.??"
        End Try

        Return strVersion

    End Function

    Public Shared Sub InitializeBaselineNoiseStats(ByRef udtBaselineNoiseStats As udtBaselineNoiseStatsType, ByVal sngMinimumBaselineNoiseLevel As Single, ByVal eNoiseThresholdMode As eNoiseThresholdModes)
        With udtBaselineNoiseStats
            .NoiseLevel = sngMinimumBaselineNoiseLevel
            .NoiseStDev = 0
            .PointsUsed = 0
            .NoiseThresholdModeUsed = eNoiseThresholdMode
        End With
    End Sub

    Private Function InterpolateX(ByRef sngInterpolatedXValue As Single, ByVal X1 As Integer, ByVal X2 As Integer, ByVal Y1 As Single, ByVal Y2 As Single, ByVal sngTargetY As Single) As Boolean
        ' Determines the X value that corresponds to sngTargetY by interpolating the line between (X1, Y1) and (X2, Y2)
        ' Returns True on success, false on error

        Dim sngDeltaY As Single
        Dim sngFraction As Single
        Dim intDeltaX As Integer
        Dim sngTargetX As Single

        sngDeltaY = Y2 - Y1                                 ' This is y-two minus y-one
        sngFraction = (sngTargetY - Y1) / sngDeltaY
        intDeltaX = X2 - X1                                 ' This is x-two minus x-one

        sngTargetX = sngFraction * intDeltaX + X1

        If Math.Abs(sngTargetX - X1) >= 0 AndAlso Math.Abs(sngTargetX - X2) >= 0 Then
            sngInterpolatedXValue = sngTargetX
            Return True
        Else
            LogErrors("clsMasicPeakFinder->InterpolateX", "TargetX is not between X1 and X2; this shouldn't happen", Nothing, True, False)
            Return False
        End If

    End Function

    Private Function InterpolateY(ByRef sngInterpolatedIntensity As Single, ByVal X1 As Integer, ByVal X2 As Integer, ByVal Y1 As Single, ByVal Y2 As Single, ByVal sngXValToInterpolate As Single) As Boolean
        ' Given two X,Y coordinates interpolate or extrapolate to determine the Y value that would be seen for a given X value

        Dim intScanDifference As Integer

        intScanDifference = X2 - X1
        If intScanDifference <> 0 Then
            sngInterpolatedIntensity = Y1 + (Y2 - Y1) * ((sngXValToInterpolate - X1) / intScanDifference)
            Return True
        Else
            ' sngXValToInterpolate is not between X1 and X2; cannot interpolate
            Return False
        End If
    End Function

    Private Sub LogErrors(ByVal strSource As String, ByVal strMessage As String, ByVal ex As Exception, Optional ByVal blnAllowInformUser As Boolean = True, Optional ByVal blnAllowThrowingException As Boolean = True, Optional ByVal blnLogLocalOnly As Boolean = True)
        Dim strMessageWithoutCRLF As String

        mStatusMessage = String.Copy(strMessage)

        strMessageWithoutCRLF = mStatusMessage.Replace(ControlChars.NewLine, "; ")

        If ex Is Nothing Then
			ex = New Exception("Error")
		Else
			If Not ex.Message Is Nothing AndAlso ex.Message.Length > 0 Then
				strMessageWithoutCRLF &= "; " & ex.Message
			End If
		End If

		Trace.WriteLine(DateTime.Now().ToLongTimeString & "; " & strMessageWithoutCRLF, strSource)
		Console.WriteLine(DateTime.Now().ToLongTimeString & "; " & strMessageWithoutCRLF, strSource)

		If Not mErrorLogger Is Nothing Then
			mErrorLogger.PostError(strMessageWithoutCRLF, ex, blnLogLocalOnly)
		End If

		If mShowMessages AndAlso blnAllowInformUser Then
			Windows.Forms.MessageBox.Show(mStatusMessage & ControlChars.NewLine & ex.Message, "Error", Windows.Forms.MessageBoxButtons.OK, Windows.Forms.MessageBoxIcon.Exclamation)
		ElseIf blnAllowThrowingException Then
			Throw New Exception(mStatusMessage, ex)
		End If
    End Sub

    Public Function LookupNoiseStatsUsingSegments(ByVal intScanIndexObserved As Integer, ByVal udtBaselineNoiseStatSegments() As udtBaselineNoiseStatSegmentsType) As udtBaselineNoiseStatsType

        Dim intNoiseSegmentIndex As Integer
        Dim intIndexSegmentA As Integer
        Dim intIndexSegmentB As Integer

        Dim udtBaselineNoiseStats As udtBaselineNoiseStatsType
        Dim intSegmentMidPointA As Integer
        Dim intSegmentMidPointB As Integer
        Dim blnMatchFound As Boolean

        Dim dblFractionFromSegmentB As Double
        Dim dblFractionFromSegmentA As Double

        Try
            If udtBaselineNoiseStatSegments Is Nothing OrElse udtBaselineNoiseStatSegments.Length < 1 Then
                InitializeBaselineNoiseStats(udtBaselineNoiseStats, GetDefaultNoiseThresholdOptions().MinimumBaselineNoiseLevel, eNoiseThresholdModes.DualTrimmedMeanByAbundance)
                Return udtBaselineNoiseStats
            End If

            ' First, initialize to the first segment
            udtBaselineNoiseStats = udtBaselineNoiseStatSegments(0).BaselineNoiseStats

            If udtBaselineNoiseStatSegments.Length > 1 Then
                ' Initialize intIndexSegmentA and intIndexSegmentB to 0, indicating no extrapolation needed
                intIndexSegmentA = 0
                intIndexSegmentB = 0
                blnMatchFound = False                ' Next, see if intScanIndexObserved matches any of the segments (provided more than one segment exists)
                For intNoiseSegmentIndex = 0 To udtBaselineNoiseStatSegments.Length - 1
                    With udtBaselineNoiseStatSegments(intNoiseSegmentIndex)
                        If intScanIndexObserved >= .SegmentIndexStart AndAlso intScanIndexObserved <= .SegmentIndexEnd Then
                            intSegmentMidPointA = .SegmentIndexStart + CInt((.SegmentIndexEnd - .SegmentIndexStart) / 2)
                            blnMatchFound = True
                        End If
                    End With

                    If blnMatchFound Then
                        udtBaselineNoiseStats = udtBaselineNoiseStatSegments(intNoiseSegmentIndex).BaselineNoiseStats

                        If intScanIndexObserved < intSegmentMidPointA Then
                            If intNoiseSegmentIndex > 0 Then
                                ' Need to Interpolate using this segment and the next one
                                intIndexSegmentA = intNoiseSegmentIndex - 1
                                intIndexSegmentB = intNoiseSegmentIndex

                                ' Copy intSegmentMidPointA to intSegmentMidPointB since the current segment is actually segment B
                                ' Define intSegmentMidPointA
                                intSegmentMidPointB = intSegmentMidPointA
                                With udtBaselineNoiseStatSegments(intNoiseSegmentIndex - 1)
                                    intSegmentMidPointA = .SegmentIndexStart + CInt((.SegmentIndexEnd - .SegmentIndexStart) / 2)
                                End With

                            Else
                                ' intScanIndexObserved occurs before the midpoint, but we're in the first segment; no need to Interpolate
                            End If
                        ElseIf intScanIndexObserved > intSegmentMidPointA Then
                            If intNoiseSegmentIndex < udtBaselineNoiseStatSegments.Length - 1 Then
                                ' Need to Interpolate using this segment and the one before it
                                intIndexSegmentA = intNoiseSegmentIndex
                                intIndexSegmentB = intNoiseSegmentIndex + 1

                                ' Note: intSegmentMidPointA is already defined since the current segment is segment A
                                ' Define intSegmentMidPointB
                                With udtBaselineNoiseStatSegments(intNoiseSegmentIndex + 1)
                                    intSegmentMidPointB = .SegmentIndexStart + CInt((.SegmentIndexEnd - .SegmentIndexStart) / 2)
                                End With

                            Else
                                ' intScanIndexObserved occurs after the midpoint, but we're in the last segment; no need to Interpolate
                            End If
                        Else
                            ' intScanIndexObserved occurs at the midpoint; no need to Interpolate
                        End If

                        If intIndexSegmentA <> intIndexSegmentB Then
                            ' Interpolate between the two segments
                            dblFractionFromSegmentB = CDbl(intScanIndexObserved - intSegmentMidPointA) / CDbl(intSegmentMidPointB - intSegmentMidPointA)
                            If dblFractionFromSegmentB < 0 Then
                                dblFractionFromSegmentB = 0
                            ElseIf dblFractionFromSegmentB > 1 Then
                                dblFractionFromSegmentB = 1
                            End If

                            dblFractionFromSegmentA = 1 - dblFractionFromSegmentB

                            ' Compute the weighted average values
                            With udtBaselineNoiseStatSegments(intIndexSegmentA).BaselineNoiseStats
                                udtBaselineNoiseStats.NoiseLevel = CSng(.NoiseLevel * dblFractionFromSegmentA + udtBaselineNoiseStatSegments(intIndexSegmentB).BaselineNoiseStats.NoiseLevel * dblFractionFromSegmentB)
                                udtBaselineNoiseStats.NoiseStDev = CSng(.NoiseStDev * dblFractionFromSegmentA + udtBaselineNoiseStatSegments(intIndexSegmentB).BaselineNoiseStats.NoiseStDev * dblFractionFromSegmentB)
                                udtBaselineNoiseStats.PointsUsed = CInt(.PointsUsed * dblFractionFromSegmentA + udtBaselineNoiseStatSegments(intIndexSegmentB).BaselineNoiseStats.PointsUsed * dblFractionFromSegmentB)
                            End With
                        End If
                        Exit For
                    End If
                Next intNoiseSegmentIndex
            End If
        Catch ex As Exception
            ' Ignore Errors
        End Try

        Return udtBaselineNoiseStats

    End Function

    Private Function ReplaceSortedDataWithMinimumPositiveValue(ByVal intDataCount As Integer, ByRef sngDataSorted() As Single) As Single
        ' This function assumes sngDataSorted() is sorted ascending
        ' It looks for the minimum positive value in sngDataSorted() and returns that value
        ' Additionally, it replaces all values of 0 in sngDataSorted() with sngMinimumPositiveValue

        Dim sngMinimumPositiveValue As Single
        Dim intIndex As Integer
        Dim intIndexFirstPositiveValue As Integer

        ' Find the minimum positive value in sngDataSorted
        ' Since it's sorted, we can stop at the first non-zero value

        intIndexFirstPositiveValue = -1
        sngMinimumPositiveValue = 0
        For intIndex = 0 To intDataCount - 1
            If sngDataSorted(intIndex) > 0 Then
                intIndexFirstPositiveValue = intIndex
                sngMinimumPositiveValue = sngDataSorted(intIndex)
                Exit For
            End If
        Next intIndex

        If sngMinimumPositiveValue < 1 Then sngMinimumPositiveValue = 1
        For intIndex = intIndexFirstPositiveValue To 0 Step -1
            sngDataSorted(intIndex) = sngMinimumPositiveValue
        Next intIndex

        Return sngMinimumPositiveValue

    End Function

    Private Function TestSignificanceUsingTTest(ByVal dblMean1 As Double, ByVal dblMean2 As Double, ByVal dblStDev1 As Double, ByVal dblStDev2 As Double, ByVal intCount1 As Integer, ByVal intCount2 As Integer, ByVal eConfidenceLevel As eTTestConfidenceLevelConstants, ByRef TCalculated As Double) As Boolean
        ' Uses the means and sigma values to compute the t-test value between the two populations to determine if they are statistically different
        ' To use the t-test you must use sample variance values, not population variance values
        ' Note: Variance_Sample = Sum((x-mean)^2) / (count-1)
        ' Note: Sigma = SquareRoot(Variance_Sample)
        '
        ' Returns True if the two populations are statistically different, based on the given significance threshold


        ' Significance Table:
        ' Confidence Levels and critical values:
        ' 80%, 90%, 95%, 98%, 99%, 99.5%, 99.8%, 99.9%
        ' 1.886, 2.920, 4.303, 6.965, 9.925, 14.089, 22.327, 31.598

        Static ConfidenceLevels() As Single = New Single() {1.886, 2.92, 4.303, 6.965, 9.925, 14.089, 22.327, 31.598}

        Dim SPooled As Double
        Dim intConfidenceLevelIndex As Integer

        If intCount1 + intCount2 <= 2 Then
            ' Cannot compute the T-Test
            TCalculated = 0
            Return False
        Else

            SPooled = Math.Sqrt(((dblStDev1 ^ 2) * (intCount1 - 1) + (dblStDev2 ^ 2) * (intCount2 - 1)) / (intCount1 + intCount2 - 2))
            TCalculated = ((dblMean1 - dblMean2) / SPooled) * Math.Sqrt(intCount1 * intCount2 / (intCount1 + intCount2))

            intConfidenceLevelIndex = eConfidenceLevel
            If intConfidenceLevelIndex < 0 Then
                intConfidenceLevelIndex = 0
            ElseIf intConfidenceLevelIndex >= ConfidenceLevels.Length Then
                intConfidenceLevelIndex = ConfidenceLevels.Length - 1
            End If

            If TCalculated >= ConfidenceLevels(intConfidenceLevelIndex) Then
                ' Differences are significant
                Return True
            Else
                ' Differences are not significant
                Return False
            End If
        End If

    End Function

    Public Sub New()
        mStatusMessage = String.Empty
        mShowMessages = False
    End Sub

End Class