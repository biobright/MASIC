using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataFilter;

namespace MagnitudeConcavityPeakFinder
{
    public class PeakDetector
    {

        private const int MINIMUM_PEAK_WIDTH = 3;

        #region Structures

        public struct udtSICPeakFinderOptionsType
        {
            /// <summary>
            /// Intensity threshold based on the maximum intensity in the data
            /// </summary>
            /// <remarks>Default: 0.01 which means the threshold is 1% of the maximum</remarks>
            public float IntensityThresholdFractionMax;

            /// <summary>
            /// Absolute minimum intensity for a point to be considered a peak
            /// </summary>
            /// <remarks>Default: 0</remarks>
            public float IntensityThresholdAbsoluteMinimum;

            /// <summary>
            /// Baseline noise determination options
            /// </summary>
            public NoiseLevelAnalyzer.udtBaselineNoiseOptionsType SICBaselineNoiseOptions;

            /// <summary>
            /// Maximum distance that the edge of an identified peak can be away from the scan number that the parent ion was observed in if the identified peak does not contain the parent ion
            /// </summary>
            /// <remarks>Default: 0</remarks>
            public int MaxDistanceScansNoOverlap;

            /// <summary>
            /// Maximum fraction of the peak maximum that an upward spike can be to be included in the peak
            /// </summary>
            /// <remarks>Default: 0.20 which means the maximum allowable spike is 20% of the peak maximum</remarks>
            public float MaxAllowedUpwardSpikeFractionMax;

            /// <summary>
            /// Multiplied by scaled S/N for the given spectrum to determine the initial minimum peak width (in scans) to try.  
            /// Scaled "S/N" = Math.Log10(Math.Floor("S/N")) * 10
            /// </summary>
            /// <remarks>Default: 0.5</remarks>
            public float InitialPeakWidthScansScaler;

            /// <summary>
            /// Maximum initial peak width to allow
            /// </summary>
            /// <remarks>Default: 30</remarks>
            public int InitialPeakWidthScansMaximum;

            /// <summary>
            /// True to smooth the data prior to finding peaks
            /// </summary>
            public bool FindPeaksOnSmoothedData;

            /// <summary>
            /// True to smooth the data, even if minimum peak width is too narrow (4 or less) 
            /// </summary>
            public bool SmoothDataRegardlessOfMinimumPeakWidth;

            /// <summary>
            /// True to use Butterworth smoothing (default)
            /// </summary>
            /// <remarks>UseButterworthSmooth takes precedence over UseSavitzkyGolaySmooth</remarks>
            public bool UseButterworthSmooth;

            public float ButterworthSamplingFrequency;

            public bool ButterworthSamplingFrequencyDoubledForSIMData;

            /// <summary>
            /// True to use Savitzky Golay smoothing
            /// </summary>
            /// <remarks>UseButterworthSmooth takes precedence over UseSavitzkyGolaySmooth</remarks>
            public bool UseSavitzkyGolaySmooth;

            /// <summary>
            /// Even number, 0 or greater; 0 means a moving average filter, 2 means a 2nd order Savitzky Golay filter
            /// </summary>
            /// <remarks>Default: 0</remarks>
            public short SavitzkyGolayFilterOrder;

            public NoiseLevelAnalyzer.udtBaselineNoiseOptionsType MassSpectraNoiseThresholdOptions;

            public bool SelectedIonMonitoringDataIsPresent;

            // When True, then the calling function is testing the smallest possible peak width
            [Obsolete("This does not appear to be used")]
            public bool TestingMinimumPeakWidth;

            /// When True, then a valid peak is one that contains udtPeakData.OriginalPeakLocationIndex, and will return only that peak
            /// When false, then finds all peaks and updates peakData.BestPeak with the best peak
            public bool ReturnClosestPeak;
        }

        public struct udtSICPotentialAreaStatsType
        {
            public double MinimumPotentialPeakArea;
            public int PeakCountBasisForMinimumPotentialArea;
        }

        #endregion

        public List<clsPeak> FindPeaks(
           udtSICPeakFinderOptionsType peakFinderOptions,
           int[] scanNumbers,
           double[] intensityData,
           int originalPeakLocationIndex,
           out List<double> smoothedYData)
        {

            return FindPeaks(peakFinderOptions, scanNumbers, intensityData, scanNumbers.Length, originalPeakLocationIndex, out smoothedYData);
        }

        public List<clsPeak> FindPeaks(
            udtSICPeakFinderOptionsType peakFinderOptions,
            int[] scanNumbers,
            double[] intensityData,
            int dataCount,
            int originalPeakLocationIndex,
            out List<double> smoothedYData)
        {
            var xyData = new List<KeyValuePair<int, double>>();

            for (int i = 0; i < dataCount; i++)
            {
                xyData.Add(new KeyValuePair<int, double>(scanNumbers[i], intensityData[i]));
            }

            return FindPeaks(peakFinderOptions, xyData, originalPeakLocationIndex, out smoothedYData);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="peakFinderOptions"></param>
        /// <param name="xyData"></param>
        /// <param name="originalPeakLocationIndex">
        /// Data point index in the x values that should be a part of the peak
        /// Used for determining the best peak</param>
        /// <param name="smoothedYData">Smoothed Y values</param>
        /// <returns></returns>
        public List<clsPeak> FindPeaks(
            udtSICPeakFinderOptionsType peakFinderOptions,
            List<KeyValuePair<int, double>> xyData,
            int originalPeakLocationIndex,
            out List<double> smoothedYData)
        {
            if (xyData.Count == 0)
            {
                smoothedYData = new List<double>();
                return new List<clsPeak>();
            }

            // Compute the potential peak area for this SIC
            var udtSICPotentialAreaStatsForPeak = FindMinimumPotentialPeakArea(xyData, peakFinderOptions);

            // Estimate the noise level
            var noiseAnalyzer = new NoiseLevelAnalyzer();
            const bool ignoreNonPositiveData = false;
            NoiseLevelAnalyzer.udtBaselineNoiseStatsType udtBaselineNoiseStats;

            var intensityData = new double[xyData.Count];
            var scanNumbers = new int[xyData.Count];
            for (var index = 0; index < xyData.Count; index++)
            {
                scanNumbers[index] = xyData[index].Key;
                intensityData[index] = xyData[index].Value;
            }

            noiseAnalyzer.ComputeTrimmedNoiseLevel(intensityData, 0, intensityData.Length - 1,
                                                   peakFinderOptions.SICBaselineNoiseOptions, ignoreNonPositiveData,
                                                   out udtBaselineNoiseStats);


            // Find maximumPotentialPeakArea and dataPointCountAboveThreshold
            int dataPointCountAboveThreshold;
            var maximumPotentialPeakArea = FindMaximumPotentialPeakArea(intensityData, peakFinderOptions, udtBaselineNoiseStats, out dataPointCountAboveThreshold);


            if (maximumPotentialPeakArea < 1)
                maximumPotentialPeakArea = 1;

            double areaBasedSignalToNoise = maximumPotentialPeakArea / udtSICPotentialAreaStatsForPeak.MinimumPotentialPeakArea;

            if (areaBasedSignalToNoise < 1)
                areaBasedSignalToNoise = 1;

            var objPeakDetector = new PeakFinder();

            var peakData = new PeakDataContainer();
            peakData.SetData(xyData);


            if (Math.Abs(peakFinderOptions.ButterworthSamplingFrequency) < float.Epsilon)
                peakFinderOptions.ButterworthSamplingFrequency = 0.25f;

            peakData.PeakWidthPointsMinimum = Convert.ToInt32(peakFinderOptions.InitialPeakWidthScansScaler * Math.Log10(Math.Floor(areaBasedSignalToNoise)) * 10);

            // Assure that .InitialPeakWidthScansMaximum is no greater than .InitialPeakWidthScansMaximum 
            //  and no greater than dataPointCountAboveThreshold/2 (rounded up)
            peakData.PeakWidthPointsMinimum = Math.Min(peakData.PeakWidthPointsMinimum, peakFinderOptions.InitialPeakWidthScansMaximum);
            peakData.PeakWidthPointsMinimum = Math.Min(peakData.PeakWidthPointsMinimum, Convert.ToInt32(Math.Ceiling(dataPointCountAboveThreshold / 2.0)));

            if (peakData.PeakWidthPointsMinimum > peakData.DataCount * 0.8)
            {
                peakData.PeakWidthPointsMinimum = Convert.ToInt32(Math.Floor(peakData.DataCount * 0.8));
            }

            if (peakData.PeakWidthPointsMinimum < MINIMUM_PEAK_WIDTH)
                peakData.PeakWidthPointsMinimum = MINIMUM_PEAK_WIDTH;

            peakData.OriginalPeakLocationIndex = originalPeakLocationIndex;

            var peakFoundContainingOriginalPeakLocation = FindPeaksWork(
                objPeakDetector,
                scanNumbers,
                peakData,
                peakFinderOptions);

            smoothedYData = peakData.SmoothedYData.ToList();

            return peakData.Peaks;

        }

        private double FindMaximumPotentialPeakArea(
            double[] intensityData,
            udtSICPeakFinderOptionsType peakFinderOptions,
            NoiseLevelAnalyzer.udtBaselineNoiseStatsType udtBaselineNoiseStats,
            out int dataPointCountAboveThreshold)
        {

            double maximumIntensity = intensityData[0];
            double maximumPotentialPeakArea = 0;

            // Initialize the intensity queue, which is used to keep track of the most recent intensity values
            var queIntensityList = new Queue();

            double potentialPeakArea = 0;
            dataPointCountAboveThreshold = 0;

            for (var index = 0; index <= intensityData.Length - 1; index++)
            {
                if (intensityData[index] > maximumIntensity)
                {
                    maximumIntensity = intensityData[index];
                }

                if (intensityData[index] < udtBaselineNoiseStats.NoiseLevel)
                {
                    continue;
                }

                // Add this intensity to potentialPeakArea
                potentialPeakArea += intensityData[index];
                if (queIntensityList.Count >= peakFinderOptions.InitialPeakWidthScansMaximum)
                {
                    // Decrement potentialPeakArea by the oldest item in the queue
                    potentialPeakArea -= Convert.ToDouble(queIntensityList.Dequeue());
                }

                // Add this intensity to the queue
                queIntensityList.Enqueue(intensityData[index]);

                if (potentialPeakArea > maximumPotentialPeakArea)
                {
                    maximumPotentialPeakArea = potentialPeakArea;
                }

                dataPointCountAboveThreshold += 1;
            }

            return maximumPotentialPeakArea;
        }

        protected double FindMinimumPositiveValue(List<KeyValuePair<int, double>> xyData, double absoluteMinimumValue)
        {

            double minimumPositiveValue = double.MaxValue;

            for (var index = 0; index < xyData.Count; index++)
            {
                if (xyData[index].Value > 0 && xyData[index].Value < minimumPositiveValue)
                    minimumPositiveValue = xyData[index].Value;
            }

            if (minimumPositiveValue < absoluteMinimumValue)
                minimumPositiveValue = absoluteMinimumValue;

            return minimumPositiveValue;

        }

        /// <summary>
        /// Computes the minimum potential peak area for a given SIC 
        /// </summary>
        /// <param name="xyData"></param>
        /// <param name="udtSICPeakFinderOptions"></param>
        /// <returns>Struct with the MinimumPotentialPeakArea</returns>
        /// <remarks>
        /// The summed intensity is not used if the number of points greater than or equal to
        ///  .SICBaselineNoiseOptions.MinimumBaselineNoiseLevel is less than Minimum_Peak_Width</remarks>
        public udtSICPotentialAreaStatsType FindMinimumPotentialPeakArea(
            List<KeyValuePair<int, double>> xyData,
            udtSICPeakFinderOptionsType udtSICPeakFinderOptions)
        {

            double minimumPotentialPeakArea = double.MaxValue;
            int peakCountBasisForMinimumPotentialArea = 0;


            if (xyData.Count == 0)
            {
                var udtEmptyAreaStats = new udtSICPotentialAreaStatsType
                {
                    MinimumPotentialPeakArea = 1,
                    PeakCountBasisForMinimumPotentialArea = 0
                };
                return udtEmptyAreaStats;
            }

            // The queue is used to keep track of the most recent intensity values
            var queIntensityList = new Queue();
            double potentialPeakArea = 0;
            int validPeakCount = 0;

            // Find the minimum intensity in intensityData()
            double minimumPositiveValue = FindMinimumPositiveValue(xyData, 1.0);

            int index;
            for (index = 0; index <= xyData.Count - 1; index++)
            {
                // If this data point is > .MinimumBaselineNoiseLevel, then add this intensity to potentialPeakArea
                //  and increment validPeakCount
                double intensityToUse = Math.Max(minimumPositiveValue, xyData[index].Value);
                if (intensityToUse >= udtSICPeakFinderOptions.SICBaselineNoiseOptions.MinimumBaselineNoiseLevel)
                {
                    potentialPeakArea += intensityToUse;
                    validPeakCount += 1;
                }

                if (queIntensityList.Count >= udtSICPeakFinderOptions.InitialPeakWidthScansMaximum)
                {
                    // Decrement potentialPeakArea by the oldest item in the queue
                    // If that item is >= .MinimumBaselineNoiseLevel, then decrement validPeakCount too
                    double dblOldestIntensity = Convert.ToDouble(queIntensityList.Dequeue());

                    if (dblOldestIntensity >= udtSICPeakFinderOptions.SICBaselineNoiseOptions.MinimumBaselineNoiseLevel && dblOldestIntensity > 0)
                    {
                        potentialPeakArea -= dblOldestIntensity;
                        validPeakCount -= 1;
                    }
                }

                // Add this intensity to the queue
                queIntensityList.Enqueue(intensityToUse);

                if (Math.Abs(potentialPeakArea) < double.Epsilon || validPeakCount < MINIMUM_PEAK_WIDTH)
                {
                    continue;
                }

                if (validPeakCount > peakCountBasisForMinimumPotentialArea)
                {
                    // The non valid peak count value is larger than the one associated with the current
                    //  minimum potential peak area; update the minimum peak area to potentialPeakArea
                    minimumPotentialPeakArea = potentialPeakArea;
                    peakCountBasisForMinimumPotentialArea = validPeakCount;
                }
                else
                {
                    if (potentialPeakArea < minimumPotentialPeakArea && validPeakCount == peakCountBasisForMinimumPotentialArea)
                    {
                        minimumPotentialPeakArea = potentialPeakArea;
                    }
                }
            }

            var udtSICPotentialAreaStats = new udtSICPotentialAreaStatsType
            {
                MinimumPotentialPeakArea = minimumPotentialPeakArea,
                PeakCountBasisForMinimumPotentialArea = peakCountBasisForMinimumPotentialArea
            };

            return udtSICPotentialAreaStats;
        }

        /// <summary>
        /// Find peaks using the data in the specified file
        /// </summary>
        /// <param name="dataFilePath"></param>
        public void TestPeakFinder(string dataFilePath)
        {
            var peakFinderOptions = GetDefaultSICPeakFinderOptions();

            TestPeakFinder(dataFilePath, peakFinderOptions);
        }

        /// <summary>
        /// Find peaks using the data in the specified file
        /// </summary>
        /// <param name="dataFilePath"></param>
        /// <param name="peakFinderOptions"></param>
        public void TestPeakFinder(string dataFilePath, udtSICPeakFinderOptionsType peakFinderOptions)
        {

            var fiDataFile = new FileInfo(dataFilePath);
            if (!fiDataFile.Exists)
            {
                Console.WriteLine("File not found: " + fiDataFile.FullName);
                return;
            }

            var xyData = new List<KeyValuePair<int, double>>();

            using (
                var reader = new StreamReader(new FileStream(fiDataFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                while (!reader.EndOfStream)
                {
                    var dataLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(dataLine))
                        continue;

                    string[] dataCols = dataLine.Split('\t');
                    if (dataCols.Length < 2)
                        continue;

                    int scanNumber;
                    double intensity;

                    if (!int.TryParse(dataCols[0], out scanNumber))
                        continue;

                    if (!double.TryParse(dataCols[1], out intensity))
                        continue;

                    xyData.Add(new KeyValuePair<int, double>(scanNumber, intensity));
                }
            }

            List<double> smoothedYData;

            // Find the peaks
            var detectedPeaks = FindPeaks(peakFinderOptions, xyData, xyData.Count / 2, out smoothedYData);

            if (detectedPeaks == null || detectedPeaks.Count == 0)
            {
                Console.WriteLine("Peak not found");
                return;
            }

            // Display the peaks
            int peakNumber = 0;
            foreach (var peak in detectedPeaks)
            {
                peakNumber++;
                Console.WriteLine("Peak " + peakNumber);
                Console.WriteLine("  Location =  " + peak.LocationIndex);
                Console.WriteLine("  Width =     " + peak.PeakWidth + " points");
                Console.WriteLine("  Area =      " + peak.Area.ToString("0.00"));
                Console.WriteLine("  LeftEdge =  " + peak.LeftEdge);
                Console.WriteLine("  RightEdge = " + peak.RightEdge);

            }

            // Write the original data, the smoothed data, and the points within each peak to a tab-delimited text file

            TestPeakFinderSaveResults(fiDataFile, detectedPeaks, xyData, smoothedYData);
        }

        private void TestPeakFinderSaveResults(FileInfo fiDataFile, List<clsPeak> detectedPeaks, List<KeyValuePair<int, double>> xyData, List<double> smoothedYData)
        {
            string resultsFilePath = Path.Combine(fiDataFile.Directory.FullName,
                                                  Path.GetFileNameWithoutExtension(fiDataFile.Name) + "_Peaks" +
                                                  fiDataFile.Extension);
            using (
                var writer = new StreamWriter(new FileStream(resultsFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                )
            {
                var outputData = new List<string>();


                writer.Write("Scan\tIntensity\tSmoothedIntensity\t");
                for (var peakIndex = 0; peakIndex < detectedPeaks.Count; peakIndex++)
                {
                    writer.Write("Peak " + (peakIndex + 1));
                    if (peakIndex < detectedPeaks.Count - 1)
                    {
                        writer.Write("\t");
                    }
                }
                writer.WriteLine();

                for (int dataValueIndex = 0; dataValueIndex < xyData.Count; dataValueIndex++)
                {
                    var dataPoint = xyData[dataValueIndex];

                    outputData.Clear();
                    outputData.Add(dataPoint.Key.ToString("0"));
                    outputData.Add(dataPoint.Value.ToString("0.000"));

                    if (dataValueIndex < smoothedYData.Count)
                        outputData.Add(smoothedYData[dataValueIndex].ToString("0.000"));
                    else
                        outputData.Add("");

                    foreach (var peak in detectedPeaks)
                    {
                        if (dataValueIndex >= peak.LeftEdge && dataValueIndex <= peak.RightEdge)
                        {
                            outputData.Add(dataPoint.Value.ToString("0.000"));
                        }
                        else
                        {
                            outputData.Add(string.Empty);
                        }
                    }

                    if (outputData.Count > 0)
                    {
                        for (int columnIndex = 0; columnIndex < outputData.Count; columnIndex++)
                        {
                            writer.Write(outputData[columnIndex]);
                            if (columnIndex < outputData.Count - 1)
                            {
                                writer.Write("\t");
                            }
                        }
                    }
                    writer.WriteLine();
                }
            }
        }

        #region GetDefaultOptions

        public static udtSICPeakFinderOptionsType GetDefaultSICPeakFinderOptions()
        {
            var peakFinderOptions = new udtSICPeakFinderOptionsType
            {
                IntensityThresholdFractionMax = 0.01f,  // 1% of the peak maximum
                IntensityThresholdAbsoluteMinimum = 0,
                SICBaselineNoiseOptions = NoiseLevelAnalyzer.GetDefaultNoiseThresholdOptions()
            };

            // Customize a few values
            peakFinderOptions.SICBaselineNoiseOptions.BaselineNoiseMode = NoiseLevelAnalyzer.eNoiseThresholdModes.TrimmedMedianByAbundance;

            peakFinderOptions.MaxDistanceScansNoOverlap = 0;

            peakFinderOptions.MaxAllowedUpwardSpikeFractionMax = 0.2f;  // 20%

            peakFinderOptions.InitialPeakWidthScansScaler = 1;
            peakFinderOptions.InitialPeakWidthScansMaximum = 30;

            peakFinderOptions.FindPeaksOnSmoothedData = true;
            peakFinderOptions.SmoothDataRegardlessOfMinimumPeakWidth = true;

            // If this is true, will ignore UseSavitzkyGolaySmooth
            peakFinderOptions.UseButterworthSmooth = true;

            peakFinderOptions.ButterworthSamplingFrequency = 0.25f;
            peakFinderOptions.ButterworthSamplingFrequencyDoubledForSIMData = true;

            peakFinderOptions.UseSavitzkyGolaySmooth = false;

            // Moving average filter if 0, Savitzky Golay filter if 2, 4, 6, etc.
            peakFinderOptions.SavitzkyGolayFilterOrder = 0;

            // Set the default Mass Spectra noise threshold options
            peakFinderOptions.MassSpectraNoiseThresholdOptions = NoiseLevelAnalyzer.GetDefaultNoiseThresholdOptions();

            // Customize a few values
            peakFinderOptions.MassSpectraNoiseThresholdOptions.BaselineNoiseMode = NoiseLevelAnalyzer.eNoiseThresholdModes.TrimmedMedianByAbundance;
            peakFinderOptions.MassSpectraNoiseThresholdOptions.TrimmedMeanFractionLowIntensityDataToAverage = 0.5f;
            peakFinderOptions.MassSpectraNoiseThresholdOptions.MinimumSignalToNoiseRatio = 2;

            peakFinderOptions.SelectedIonMonitoringDataIsPresent = false;
            peakFinderOptions.TestingMinimumPeakWidth = false;
            peakFinderOptions.ReturnClosestPeak = true;

            return peakFinderOptions;

        }

        #endregion


        private void ExamineNarrowPeaks(PeakDataContainer peakData, udtSICPeakFinderOptionsType peakFinderOptions)
        {
            if (peakData.Peaks.Count <= 0)
            {
                // No peaks were found; create a new peak list using the original peak location index as the peak center
                peakData.Peaks = new List<clsPeak>
                {
                    new clsPeak(peakData.OriginalPeakLocationIndex)
                };

                return;
            }

            if (!peakFinderOptions.ReturnClosestPeak)
            {
                return;
            }

            // Make sure one of the peaks is within 1 of the original peak location 
            bool blnSuccess = false;
            foreach (var peak in peakData.Peaks)
            {
                if (peak.LocationIndex - peakData.OriginalPeakLocationIndex <= 1)
                {
                    blnSuccess = true;
                    break;
                }
            }

            if (blnSuccess)
            {
                // One of the peaks includes data point peakData.OriginalPeakLocationIndex
                return;
            }

            // None of the peaks includes peakData.OriginalPeakLocationIndex
            var newPeak = new clsPeak(peakData.OriginalPeakLocationIndex)
            {
                Area = peakData.YData[peakData.OriginalPeakLocationIndex]
            };

            peakData.Peaks.Add(newPeak);
        }

        private void ExpandPeakLeftEdge(
            PeakDataContainer peakData,
            udtSICPeakFinderOptionsType peakFinderOptions,
            clsPeak peak,
            float sngPeakMaximum,
            bool dataIsSmoothed)
        {
            int intStepOverIncreaseCount = 0;
            while (peak.LeftEdge > 0)
            {

                if (peakData.SmoothedYData[peak.LeftEdge - 1] <
                    peakData.SmoothedYData[peak.LeftEdge])
                {
                    // The adjacent point is lower than the current point
                    peak.LeftEdge -= 1;
                }
                else if (
                    Math.Abs(peakData.SmoothedYData[peak.LeftEdge - 1] -
                             peakData.SmoothedYData[peak.LeftEdge]) < double.Epsilon)
                {
                    // The adjacent point is equal to the current point
                    peak.LeftEdge -= 1;
                }
                else
                {
                    // The next point to the left is not lower; what about the point after it?
                    if (peak.LeftEdge > 1)
                    {
                        if (peakData.SmoothedYData[peak.LeftEdge - 2] <= peakData.SmoothedYData[peak.LeftEdge])
                        {
                            // Only allow ignoring an upward spike if the delta from this point to the next is <= .MaxAllowedUpwardSpikeFractionMax of sngPeakMaximum
                            if (peakData.SmoothedYData[peak.LeftEdge - 1] -
                                peakData.SmoothedYData[peak.LeftEdge] >
                                peakFinderOptions.MaxAllowedUpwardSpikeFractionMax * sngPeakMaximum)
                            {
                                break;
                            }

                            if (dataIsSmoothed)
                            {
                                // Only ignore an upward spike twice if the data is smoothed
                                if (intStepOverIncreaseCount >= 2)
                                {
                                    break;
                                }
                            }

                            peak.LeftEdge -= 1;

                            intStepOverIncreaseCount += 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private void ExpandPeakRightEdge(
            PeakDataContainer peakData,
            udtSICPeakFinderOptionsType peakFinderOptions,
            clsPeak peak,
            float sngPeakMaximum,
            bool dataIsSmoothed)
        {
            int intStepOverIncreaseCount = 0;
            while (peak.RightEdge < peakData.DataCount - 1)
            {

                if (peakData.SmoothedYData[peak.RightEdge + 1] < peakData.SmoothedYData[peak.RightEdge])
                {
                    // The adjacent point is lower than the current point
                    peak.RightEdge += 1;
                }
                else if (
                    Math.Abs(peakData.SmoothedYData[peak.RightEdge + 1] -
                             peakData.SmoothedYData[peak.RightEdge]) < double.Epsilon)
                {
                    // The adjacent point is equal to the current point
                    peak.RightEdge += 1;
                }
                else
                {
                    // The next point to the right is not lower; what about the point after it?
                    if (peak.RightEdge < peakData.DataCount - 2)
                    {
                        if (peakData.SmoothedYData[peak.RightEdge + 2] <=
                            peakData.SmoothedYData[peak.RightEdge])
                        {
                            // Only allow ignoring an upward spike if the delta from this point to the next is <= .MaxAllowedUpwardSpikeFractionMax of sngPeakMaximum
                            if (peakData.SmoothedYData[peak.RightEdge + 1] -
                                peakData.SmoothedYData[peak.RightEdge] >
                                peakFinderOptions.MaxAllowedUpwardSpikeFractionMax * sngPeakMaximum)
                            {
                                break;
                            }

                            if (dataIsSmoothed)
                            {
                                // Only ignore an upward spike twice if the data is smoothed
                                if (intStepOverIncreaseCount >= 2)
                                {
                                    break;
                                }
                            }

                            peak.RightEdge += 1;

                            intStepOverIncreaseCount += 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="objPeakDetector"></param>
        /// <param name="scanNumbers"></param>
        /// <param name="peakData"></param>
        /// <param name="peakFinderOptions"></param>
        /// <returns>Detected peaks will be in the peakData object</returns>
        private bool FindPeaksWork(
            PeakFinder objPeakDetector,
            int[] scanNumbers,
            PeakDataContainer peakData,
            udtSICPeakFinderOptionsType peakFinderOptions)
        {

            const float sngPeakMaximum = 0;

            bool validPeakFound;

            string errorMessage;

            // Smooth the Y data, and store in peakData.SmoothedYData
            // Note that if using a Butterworth filter, then we increase peakData.PeakWidthPointsMinimum if too small, compared to 1/SamplingFrequency
            int peakWidthPointsMinimum = peakData.PeakWidthPointsMinimum;
            double[] smoothedYData;

            bool dataIsSmoothed = SmoothData(peakData.YData, peakData.DataCount, peakFinderOptions, ref peakWidthPointsMinimum, out smoothedYData, out errorMessage);

            // peakWidthPointsMinimum may have been auto-updated
            peakData.PeakWidthPointsMinimum = peakWidthPointsMinimum;

            // Store the smoothed data in the data container
            peakData.SetSmoothedData(smoothedYData);

            int peakDetectIntensityThresholdPercentageOfMaximum = Convert.ToInt32(peakFinderOptions.IntensityThresholdFractionMax * 100);
            const int peakWidthInSigma = 2;
            const bool useValleysForPeakWidth = true;
            const bool movePeakLocationToMaxIntensity = true;


            if (peakFinderOptions.FindPeaksOnSmoothedData && dataIsSmoothed)
            {
                peakData.Peaks = objPeakDetector.DetectPeaks(
                    peakData.XData, peakData.SmoothedYData,
                    peakFinderOptions.IntensityThresholdAbsoluteMinimum,
                    peakData.PeakWidthPointsMinimum,
                    peakDetectIntensityThresholdPercentageOfMaximum,
                    peakWidthInSigma,
                    useValleysForPeakWidth,
                    movePeakLocationToMaxIntensity);
            }
            else
            {
                // Look for the peaks, using peakData.PeakWidthPointsMinimum as the minimum peak width 
                peakData.Peaks = objPeakDetector.DetectPeaks(
                    peakData.XData, peakData.YData,
                    peakFinderOptions.IntensityThresholdAbsoluteMinimum,
                    peakData.PeakWidthPointsMinimum,
                    peakDetectIntensityThresholdPercentageOfMaximum,
                    peakWidthInSigma,
                    useValleysForPeakWidth,
                    movePeakLocationToMaxIntensity);
            }


            if (peakData.Peaks == null)
            {
                // Fatal error occurred while finding peaks
                return false;
            }

            if (peakData.PeakWidthPointsMinimum == MINIMUM_PEAK_WIDTH)
            {
                // Testing the minimum peak width; run some checks
                ExamineNarrowPeaks(peakData, peakFinderOptions);
            }

            if (peakData.Peaks.Count <= 0)
            {
                // No peaks were found
                return false;

            }

            foreach (var peak in peakData.Peaks)
            {
                peak.IsValid = false;

                // Find the center and boundaries of this peak

                // Make sure peak.LocationIndex is between peak.LeftEdge and peak.RightEdge
                if (peak.LeftEdge > peak.LocationIndex)
                {
                    Console.WriteLine("peak.LeftEdge is > peak.LocationIndex; this is probably a programming error");
                    peak.LeftEdge = peak.LocationIndex;
                }

                if (peak.RightEdge < peak.LocationIndex)
                {
                    Console.WriteLine("peak.RightEdge is < peak.LocationIndex; this is probably a programming error");
                    peak.RightEdge = peak.LocationIndex;
                }

                // See if the peak boundaries (left and right edges) need to be narrowed or expanded 
                // Do this by stepping left or right while the intensity is decreasing.  If an increase is found, but the 
                // next point after the increasing point is less than the current point, then possibly keep stepping; the 
                // test for whether to keep stepping is that the next point away from the increasing point must be less 
                // than the current point.  If this is the case, replace the increasing point with the average of the 
                // current point and the point two points away
                //
                // Use smoothed data for this step
                // Determine the smoothing window based on peakData.PeakWidthPointsMinimum
                // If peakData.PeakWidthPointsMinimum <= 4 then do not filter

                if (!dataIsSmoothed)
                {
                    // Need to smooth the data now
                    peakWidthPointsMinimum = peakData.PeakWidthPointsMinimum;

                    dataIsSmoothed = SmoothData(
                        peakData.YData,
                        peakData.DataCount,
                        peakFinderOptions,
                        ref peakWidthPointsMinimum,
                        out smoothedYData,
                        out errorMessage);

                    // peakWidthPointsMinimum may have been auto-updated
                    peakData.PeakWidthPointsMinimum = peakWidthPointsMinimum;

                    // Store the smoothed data in the data container
                    peakData.SetSmoothedData(smoothedYData);

                }

                // First see if we need to narrow the peak by looking for decreasing intensities moving toward the peak center
                // We'll use the unsmoothed data for this
                while (peak.LeftEdge < peak.LocationIndex - 1)
                {
                    if (peakData.YData[peak.LeftEdge] > peakData.YData[peak.LeftEdge + 1])
                    {
                        // OrElse (usedSmoothedDataForPeakDetection AndAlso peakData.SmoothedYData[peak.LeftEdge) < 0) Then
                        peak.LeftEdge += 1;
                    }
                    else
                    {
                        break;
                    }
                }

                while (peak.RightEdge > peak.LocationIndex + 1)
                {
                    if (peakData.YData[peak.RightEdge - 1] < peakData.YData[peak.RightEdge])
                    {
                        // OrElse (usedSmoothedDataForPeakDetection AndAlso peakData.SmoothedYData[peak.RightEdge) < 0) Then
                        peak.RightEdge -= 1;
                    }
                    else
                    {
                        break;
                    }
                }


                // Now see if we need to expand the peak by looking for decreasing intensities moving away from the peak center, 
                //  but allowing for small increases
                // We'll use the smoothed data for this; if we encounter negative values in the smoothed data, we'll keep going until we reach the low point since huge peaks can cause some odd behavior with the Butterworth filter
                // Keep track of the number of times we step over an increased value

                ExpandPeakLeftEdge(peakData, peakFinderOptions, peak, sngPeakMaximum, dataIsSmoothed);

                ExpandPeakRightEdge(peakData, peakFinderOptions, peak, sngPeakMaximum, dataIsSmoothed);

                peak.IsValid = true;
                if (!peakFinderOptions.ReturnClosestPeak)
                {
                    continue;
                }

                // If peakData.OriginalPeakLocationIndex is not between peak.LeftEdge and peak.RightEdge, then check
                //  if the scan number for peakData.OriginalPeakLocationIndex is within .MaxDistanceScansNoOverlap scans of
                //  either of the peak edges; if not, then mark the peak as invalid since it does not contain the 
                //  scan for the parent ion
                if (peakData.OriginalPeakLocationIndex < peak.LeftEdge)
                {
                    if (
                        Math.Abs(scanNumbers[peakData.OriginalPeakLocationIndex] -
                                 scanNumbers[peak.LeftEdge]) >
                        peakFinderOptions.MaxDistanceScansNoOverlap)
                    {
                        peak.IsValid = false;
                    }
                }
                else if (peakData.OriginalPeakLocationIndex > peak.RightEdge)
                {
                    if (
                        Math.Abs(scanNumbers[peakData.OriginalPeakLocationIndex] -
                                 scanNumbers[peak.RightEdge]) >
                        peakFinderOptions.MaxDistanceScansNoOverlap)
                    {
                        peak.IsValid = false;
                    }
                }
            }

            // Find the peak with the largest area that has peakData.PeakIsValid = True
            peakData.BestPeak = null;
            double bestPeakArea = double.MinValue;
            foreach (var peak in peakData.Peaks)
            {
                if (peak.IsValid)
                {
                    if (peak.Area > bestPeakArea)
                    {
                        peakData.BestPeak = peak;
                        bestPeakArea = peak.Area;
                    }
                }
            }

            if (peakData.BestPeak != null)
            {
                validPeakFound = true;
            }
            else
            {
                validPeakFound = false;
            }

            return validPeakFound;

        }

        private bool SmoothData(
            double[] yDataVals,
            int dataCount,
            udtSICPeakFinderOptionsType peakFinderOptions,
            ref int peakWidthPointsMinimum,
            out double[] smoothedYData,
            out string errorMessage)
        {
            // Returns True if the data was smoothed; false if not or an error
            // The smoothed data is returned in udtPeakData.SmoothedYData

            errorMessage = string.Empty;

            // Make a copy of the data
            smoothedYData = new double[dataCount];
            yDataVals.CopyTo(smoothedYData, 0);

            bool performSmooth = peakFinderOptions.SmoothDataRegardlessOfMinimumPeakWidth;

            if (peakWidthPointsMinimum > 4)
            {
                if (peakFinderOptions.UseSavitzkyGolaySmooth || peakFinderOptions.UseButterworthSmooth)
                    performSmooth = true;
            }

            if (!performSmooth)
            {
                // Do not smooth
                return false;
            }


            bool success;
            if (peakFinderOptions.UseButterworthSmooth)
            {
                success = SmoothDataButterworth(smoothedYData, dataCount, peakFinderOptions, ref peakWidthPointsMinimum, out errorMessage);
            }
            else
            {
                success = SmoothDataSavitkyGolay(smoothedYData, dataCount, peakFinderOptions, peakWidthPointsMinimum, out errorMessage);
            }

            return success;

        }

        private static bool SmoothDataButterworth(
            double[] smoothedYData,
            int dataCount,
            udtSICPeakFinderOptionsType peakFinderOptions,
            ref int peakWidthPointsMinimum,
            out string errorMessage)
        {

            var dataFilter = new clsDataFilter();
            errorMessage = string.Empty;

            // Filter the data with a Butterworth filter (.UseButterworthSmooth takes precedence over .UseSavitzkyGolaySmooth)
            float butterWorthFrequency;
            if (peakFinderOptions.SelectedIonMonitoringDataIsPresent && peakFinderOptions.ButterworthSamplingFrequencyDoubledForSIMData)
            {
                butterWorthFrequency = peakFinderOptions.ButterworthSamplingFrequency * 2;
            }
            else
            {
                butterWorthFrequency = peakFinderOptions.ButterworthSamplingFrequency;
            }

            const int startIndex = 0;
            int endIndex = dataCount - 1;

            bool success = dataFilter.ButterworthFilter(ref smoothedYData, startIndex, endIndex, butterWorthFrequency);
            if (!success)
            {
                Console.WriteLine("Error with the Butterworth filter" + errorMessage);
                return false;
            }

            // Data was smoothed
            // Validate that peakWidthPointsMinimum is large enough
            if (butterWorthFrequency > 0)
            {
                int peakWidthPointsCompare = Convert.ToInt32(Math.Round(1 / butterWorthFrequency, 0));
                if (peakWidthPointsMinimum < peakWidthPointsCompare)
                {
                    peakWidthPointsMinimum = peakWidthPointsCompare;
                }
            }

            return true;
        }

        private bool SmoothDataSavitkyGolay(
            double[] smoothedYData,
            int dataCount,
            udtSICPeakFinderOptionsType peakFinderOptions,
            int peakWidthPointsMinimum,
            out string errorMessage)
        {
            var dataFilter = new DataFilter.clsDataFilter();
            errorMessage = string.Empty;

            // Filter the data with a Savitzky Golay filter
            int intFilterThirdWidth = Convert.ToInt32(Math.Floor(peakWidthPointsMinimum / 3.0));
            if (intFilterThirdWidth > 3)
                intFilterThirdWidth = 3;

            // Make sure intFilterThirdWidth is Odd
            if (intFilterThirdWidth % 2 == 0)
            {
                intFilterThirdWidth -= 1;
            }

            const int startIndex = 0;
            int endIndex = dataCount - 1;

            // Note that the SavitzkyGolayFilter doesn't work right for PolynomialDegree values greater than 0
            // Also note that a PolynomialDegree value of 0 results in the equivalent of a moving average filter
            var success = dataFilter.SavitzkyGolayFilter(ref smoothedYData, startIndex,
                                                       endIndex, intFilterThirdWidth,
                                                       intFilterThirdWidth,
                                                       peakFinderOptions.SavitzkyGolayFilterOrder, true,
                                                       ref errorMessage);
            if (!success)
            {
                Console.WriteLine("Error with the Savitzky-Golay filter: " + errorMessage);
                return false;
            }

            // Data was smoothed
            return true;
        }
    }
}