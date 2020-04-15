﻿namespace MASICPeakFinder
{
    public class clsBaselineNoiseOptions
    {
        /// <summary>
        /// Method to use to determine the baseline noise level
        /// </summary>
        public clsMASICPeakFinder.eNoiseThresholdModes BaselineNoiseMode;

        /// <summary>
        /// Explicitly defined noise intensity
        /// </summary>
        /// <remarks>Only used if .BaselineNoiseMode = eNoiseThresholdModes.AbsoluteThreshold; 50000 for SIC, 0 for MS/MS spectra</remarks>
        public double BaselineNoiseLevelAbsolute;

        /// <summary>
        /// Minimum signal/noise ratio
        /// </summary>
        /// <remarks>Typically 2 or 3 for spectra; 0 for SICs</remarks>
        public double MinimumSignalToNoiseRatio;

        /// <summary>
        /// If the noise threshold computed is less than this value, then this value is used to compute S/N
        /// Additionally, this is used as the minimum intensity threshold when computing a trimmed noise level
        /// </summary>
        public double MinimumBaselineNoiseLevel;

        /// <summary>
        /// Typically 0.75 for SICs, 0.5 for MS/MS spectra
        /// Only used for eNoiseThresholdModes.TrimmedMeanByAbundance, .TrimmedMeanByCount, .TrimmedMedianByAbundance
        /// </summary>
        public double TrimmedMeanFractionLowIntensityDataToAverage;

        /// <summary>
        /// Typically 5; distance from the mean in standard deviation units (SqrRt(Variance)) to discard data for computing the trimmed mean
        /// </summary>
        public short DualTrimmedMeanStdDevLimits;

        /// <summary>
        /// Typically 3; set to 1 to disable segmentation
        /// </summary>
        public short DualTrimmedMeanMaximumSegments;

        /// <summary>
        /// Return a new instance of clsBaselineNoiseOptions with copied options
        /// </summary>
        /// <returns></returns>
        public clsBaselineNoiseOptions Clone()
        {
            return (clsBaselineNoiseOptions)MemberwiseClone();
        }
    }
}