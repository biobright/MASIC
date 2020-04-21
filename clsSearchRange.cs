﻿using System;
using System.Runtime.InteropServices;

namespace MASIC
{
    /// <summary>
    /// This class can be used to search a list of values for a given value, plus or minus a given tolerance
    /// The input list need not be sorted, since mPointerIndices() will be populated when the data is loaded,
    /// after which the data array will be sorted
    ///
    /// To prevent this behavior, and save memory by not populating mPointerIndices, set mUsePointerIndexArray = False
    /// </summary>
    public class clsSearchRange
    {
        public clsSearchRange()
        {
            InitializeLocalVariables();
        }

        #region "Constants and Enums"
        private enum eDataTypeToUse
        {
            NoDataPresent = 0,
            IntegerType = 1,
            SingleType = 2,
            DoubleType = 3
        }
        #endregion

        #region "Classwide Variables"
        private eDataTypeToUse mDataType;

        private int[] mDataInt;
        private float[] mDataSingle;
        private double[] mDataDouble;

        private int[] mPointerIndices;        // Pointers to the original index of the data point in the source array

        private bool mPointerArrayIsValid;
        private bool mUsePointerIndexArray;    // Set this to false to conserve memory usage

        #endregion

        #region "Interface Functions"
        public int DataCount
        {
            get
            {
                switch (mDataType)
                {
                    case eDataTypeToUse.IntegerType:
                        return mDataInt.Length;
                    case eDataTypeToUse.SingleType:
                        return mDataSingle.Length;
                    case eDataTypeToUse.DoubleType:
                        return mDataDouble.Length;
                    case eDataTypeToUse.NoDataPresent:
                        return 0;
                    default:
                        throw new Exception("Unknown data type encountered: " + mDataType.ToString());
                        break;
                }
            }
        }

        public int get_OriginalIndex(int index)
        {
            if (mPointerArrayIsValid)
            {
                try
                {
                    if (index < mPointerIndices.Length)
                    {
                        return mPointerIndices[index];
                    }
                    else
                    {
                        return -1;
                    }
                }
                catch (Exception ex)
                {
                    return -1;
                }
            }
            else
            {
                return -1;
            }
        }

        public bool UsePointerIndexArray
        {
            get => mUsePointerIndexArray;
            set => mUsePointerIndexArray = value;
        }
        #endregion

        #region "Binary Search Range"

        private void BinarySearchRangeInt(int searchValue, int toleranceHalfWidth, ref int matchIndexStart, ref int matchIndexEnd)
        {
            // Recursive search function

            int indexMidpoint;
            var leftDone = default(bool);
            var rightDone = default(bool);
            int leftIndex;
            int rightIndex;

            indexMidpoint = (matchIndexStart + matchIndexEnd) / 2;
            if (indexMidpoint == matchIndexStart)
            {
                // Min and Max are next to each other
                if (Math.Abs(searchValue - mDataInt[matchIndexStart]) > toleranceHalfWidth)
                    matchIndexStart = matchIndexEnd;
                if (Math.Abs(searchValue - mDataInt[matchIndexEnd]) > toleranceHalfWidth)
                    matchIndexEnd = indexMidpoint;
                return;
            }

            if (mDataInt[indexMidpoint] > searchValue + toleranceHalfWidth)
            {
                // Out of range on the right
                matchIndexEnd = indexMidpoint;
                BinarySearchRangeInt(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else if (mDataInt[indexMidpoint] < searchValue - toleranceHalfWidth)
            {
                // Out of range on the left
                matchIndexStart = indexMidpoint;
                BinarySearchRangeInt(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else
            {
                // Inside range; figure out the borders
                leftIndex = indexMidpoint;
                do
                {
                    leftIndex = leftIndex - 1;
                    if (leftIndex < matchIndexStart)
                    {
                        leftDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataInt[leftIndex]) > toleranceHalfWidth)
                        leftDone = true;
                }
                while (!leftDone);
                rightIndex = indexMidpoint;
                do
                {
                    rightIndex = rightIndex + 1;
                    if (rightIndex > matchIndexEnd)
                    {
                        rightDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataInt[rightIndex]) > toleranceHalfWidth)
                        rightDone = true;
                }
                while (!rightDone);

                matchIndexStart = leftIndex + 1;
                matchIndexEnd = rightIndex - 1;
            }
        }

        private void BinarySearchRangeSng(float searchValue, float toleranceHalfWidth, ref int matchIndexStart, ref int matchIndexEnd)
        {
            // Recursive search function

            int indexMidpoint;
            var leftDone = default(bool);
            var rightDone = default(bool);
            int leftIndex;
            int rightIndex;

            indexMidpoint = (matchIndexStart + matchIndexEnd) / 2;
            if (indexMidpoint == matchIndexStart)
            {
                // Min and Max are next to each other
                if (Math.Abs(searchValue - mDataSingle[matchIndexStart]) > toleranceHalfWidth)
                    matchIndexStart = matchIndexEnd;
                if (Math.Abs(searchValue - mDataSingle[matchIndexEnd]) > toleranceHalfWidth)
                    matchIndexEnd = indexMidpoint;
                return;
            }

            if (mDataSingle[indexMidpoint] > searchValue + toleranceHalfWidth)
            {
                // Out of range on the right
                matchIndexEnd = indexMidpoint;
                BinarySearchRangeSng(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else if (mDataSingle[indexMidpoint] < searchValue - toleranceHalfWidth)
            {
                // Out of range on the left
                matchIndexStart = indexMidpoint;
                BinarySearchRangeSng(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else
            {
                // Inside range; figure out the borders
                leftIndex = indexMidpoint;
                do
                {
                    leftIndex = leftIndex - 1;
                    if (leftIndex < matchIndexStart)
                    {
                        leftDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataSingle[leftIndex]) > toleranceHalfWidth)
                        leftDone = true;
                }
                while (!leftDone);
                rightIndex = indexMidpoint;
                do
                {
                    rightIndex = rightIndex + 1;
                    if (rightIndex > matchIndexEnd)
                    {
                        rightDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataSingle[rightIndex]) > toleranceHalfWidth)
                        rightDone = true;
                }
                while (!rightDone);
                matchIndexStart = leftIndex + 1;
                matchIndexEnd = rightIndex - 1;
            }
        }

        private void BinarySearchRangeDbl(double searchValue, double toleranceHalfWidth, ref int matchIndexStart, ref int matchIndexEnd)
        {
            // Recursive search function

            int indexMidpoint;
            var leftDone = default(bool);
            var rightDone = default(bool);
            int leftIndex;
            int rightIndex;

            indexMidpoint = (matchIndexStart + matchIndexEnd) / 2;
            if (indexMidpoint == matchIndexStart)
            {
                // Min and Max are next to each other
                if (Math.Abs(searchValue - mDataDouble[matchIndexStart]) > toleranceHalfWidth)
                    matchIndexStart = matchIndexEnd;
                if (Math.Abs(searchValue - mDataDouble[matchIndexEnd]) > toleranceHalfWidth)
                    matchIndexEnd = indexMidpoint;
                return;
            }

            if (mDataDouble[indexMidpoint] > searchValue + toleranceHalfWidth)
            {
                // Out of range on the right
                matchIndexEnd = indexMidpoint;
                BinarySearchRangeDbl(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else if (mDataDouble[indexMidpoint] < searchValue - toleranceHalfWidth)
            {
                // Out of range on the left
                matchIndexStart = indexMidpoint;
                BinarySearchRangeDbl(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
            }
            else
            {
                // Inside range; figure out the borders
                leftIndex = indexMidpoint;
                do
                {
                    leftIndex = leftIndex - 1;
                    if (leftIndex < matchIndexStart)
                    {
                        leftDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataDouble[leftIndex]) > toleranceHalfWidth)
                        leftDone = true;
                }
                while (!leftDone);
                rightIndex = indexMidpoint;
                do
                {
                    rightIndex = rightIndex + 1;
                    if (rightIndex > matchIndexEnd)
                    {
                        rightDone = true;
                    }
                    else if (Math.Abs(searchValue - mDataDouble[rightIndex]) > toleranceHalfWidth)
                        rightDone = true;
                }
                while (!rightDone);
                matchIndexStart = leftIndex + 1;
                matchIndexEnd = rightIndex - 1;
            }
        }
        #endregion

        private void ClearUnusedData()
        {
            if (mDataType != eDataTypeToUse.IntegerType)
                mDataInt = new int[0];
            if (mDataType != eDataTypeToUse.SingleType)
                mDataSingle = new float[0];
            if (mDataType != eDataTypeToUse.DoubleType)
                mDataDouble = new double[0];
            if (mDataType == eDataTypeToUse.NoDataPresent)
            {
                mPointerArrayIsValid = false;
            }
        }

        public void ClearData()
        {
            mDataType = eDataTypeToUse.NoDataPresent;
            ClearUnusedData();
        }

        #region "Fill with Data"

        public bool FillWithData(ref int[] values)
        {
            bool success;
            try
            {
                if (values == null || values.Length == 0)
                {
                    success = false;
                }
                else
                {
                    mDataInt = new int[values.Length];
                    values.CopyTo(mDataInt, 0);

                    if (mUsePointerIndexArray)
                    {
                        InitializePointerIndexArray(mDataInt.Length);
                        Array.Sort(mDataInt, mPointerIndices);
                    }
                    else
                    {
                        Array.Sort(mDataInt);
                        mPointerArrayIsValid = false;
                    }

                    mDataType = eDataTypeToUse.IntegerType;
                    success = true;
                }
            }
            catch (Exception ex)
            {
                success = false;
            }

            if (success)
                ClearUnusedData();
            return success;
        }

        public bool FillWithData(ref float[] values)
        {
            bool success;
            try
            {
                if (values == null || values.Length == 0)
                {
                    success = false;
                }
                else
                {
                    mDataSingle = new float[values.Length];
                    values.CopyTo(mDataSingle, 0);

                    if (mUsePointerIndexArray)
                    {
                        InitializePointerIndexArray(mDataSingle.Length);
                        Array.Sort(mDataSingle, mPointerIndices);
                    }
                    else
                    {
                        Array.Sort(mDataSingle);
                        mPointerArrayIsValid = false;
                    }

                    mDataType = eDataTypeToUse.SingleType;
                    success = true;
                }
            }
            catch (Exception ex)
            {
                success = false;
            }

            if (success)
                ClearUnusedData();
            return success;
        }

        public bool FillWithData(ref double[] values)
        {
            bool success;
            try
            {
                if (values == null || values.Length == 0)
                {
                    success = false;
                }
                else
                {
                    mDataDouble = new double[values.Length];
                    values.CopyTo(mDataDouble, 0);

                    if (mUsePointerIndexArray)
                    {
                        InitializePointerIndexArray(mDataDouble.Length);
                        Array.Sort(mDataDouble, mPointerIndices);
                    }
                    else
                    {
                        Array.Sort(mDataDouble);
                        mPointerArrayIsValid = false;
                    }

                    mDataType = eDataTypeToUse.DoubleType;
                    success = true;
                }
            }
            catch (Exception ex)
            {
                success = false;
            }

            if (success)
                ClearUnusedData();
            return success;
        }
        #endregion

        #region "Find Value Range"

        public bool FindValueRange(int searchValue, int toleranceHalfWidth, [Optional, DefaultParameterValue(0)] ref int matchIndexStart, [Optional, DefaultParameterValue(0)] ref int matchIndexEnd)
        {
            // Searches the loaded data for searchValue with a tolerance of +/-toleranceHalfWidth
            // Returns True if a match is found; in addition, populates matchIndexStart and matchIndexEnd
            // Otherwise, returns false

            bool matchFound;

            if (mDataType != eDataTypeToUse.IntegerType)
            {
                switch (mDataType)
                {
                    case eDataTypeToUse.SingleType:
                        matchFound = FindValueRange(Convert.ToSingle(searchValue), Convert.ToSingle(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    case eDataTypeToUse.DoubleType:
                        matchFound = FindValueRange(Convert.ToDouble(searchValue), Convert.ToDouble(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    default:
                        matchFound = false;
                        break;
                }
            }
            else
            {
                matchIndexStart = 0;
                matchIndexEnd = mDataInt.Length - 1;

                if (mDataInt.Length == 0)
                {
                    matchIndexEnd = -1;
                }
                else if (mDataInt.Length == 1)
                {
                    if (Math.Abs(searchValue - mDataInt[0]) > toleranceHalfWidth)
                    {
                        // Only one data point, and it is not within tolerance
                        matchIndexEnd = -1;
                    }
                }
                else
                {
                    BinarySearchRangeInt(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
                }

                if (matchIndexStart > matchIndexEnd)
                {
                    matchIndexStart = -1;
                    matchIndexEnd = -1;
                    matchFound = false;
                }
                else
                {
                    matchFound = true;
                }
            }

            return matchFound;
        }

        public bool FindValueRange(double searchValue, double toleranceHalfWidth, [Optional, DefaultParameterValue(0)] ref int matchIndexStart, [Optional, DefaultParameterValue(0)] ref int matchIndexEnd)
        {
            // Searches the loaded data for searchValue with a tolerance of +/-tolerance
            // Returns True if a match is found; in addition, populates matchIndexStart and matchIndexEnd
            // Otherwise, returns false

            bool matchFound;

            if (mDataType != eDataTypeToUse.DoubleType)
            {
                switch (mDataType)
                {
                    case eDataTypeToUse.IntegerType:
                        matchFound = FindValueRange(Convert.ToInt32(searchValue), Convert.ToInt32(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    case eDataTypeToUse.SingleType:
                        matchFound = FindValueRange(Convert.ToSingle(searchValue), Convert.ToSingle(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    default:
                        matchFound = false;
                        break;
                }
            }
            else
            {
                matchIndexStart = 0;
                matchIndexEnd = mDataDouble.Length - 1;

                if (mDataDouble.Length == 0)
                {
                    matchIndexEnd = -1;
                }
                else if (mDataDouble.Length == 1)
                {
                    if (Math.Abs(searchValue - mDataDouble[0]) > toleranceHalfWidth)
                    {
                        // Only one data point, and it is not within tolerance
                        matchIndexEnd = -1;
                    }
                }
                else
                {
                    BinarySearchRangeDbl(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
                }

                if (matchIndexStart > matchIndexEnd)
                {
                    matchIndexStart = -1;
                    matchIndexEnd = -1;
                    matchFound = false;
                }
                else
                {
                    matchFound = true;
                }
            }

            return matchFound;
        }

        public bool FindValueRange(float searchValue, float toleranceHalfWidth, [Optional, DefaultParameterValue(0)] ref int matchIndexStart, [Optional, DefaultParameterValue(0)] ref int matchIndexEnd)
        {
            // Searches the loaded data for searchValue with a tolerance of +/-tolerance
            // Returns True if a match is found; in addition, populates matchIndexStart and matchIndexEnd
            // Otherwise, returns false

            bool matchFound;

            if (mDataType != eDataTypeToUse.SingleType)
            {
                switch (mDataType)
                {
                    case eDataTypeToUse.IntegerType:
                        matchFound = FindValueRange(Convert.ToInt32(searchValue), Convert.ToInt32(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    case eDataTypeToUse.DoubleType:
                        matchFound = FindValueRange(Convert.ToDouble(searchValue), Convert.ToDouble(toleranceHalfWidth), ref matchIndexStart, ref matchIndexEnd);
                        break;
                    default:
                        matchFound = false;
                        break;
                }
            }
            else
            {
                matchIndexStart = 0;
                matchIndexEnd = mDataSingle.Length - 1;

                if (mDataSingle.Length == 0)
                {
                    matchIndexEnd = -1;
                }
                else if (mDataSingle.Length == 1)
                {
                    if (Math.Abs(searchValue - mDataSingle[0]) > toleranceHalfWidth)
                    {
                        // Only one data point, and it is not within tolerance
                        matchIndexEnd = -1;
                    }
                }
                else
                {
                    BinarySearchRangeSng(searchValue, toleranceHalfWidth, ref matchIndexStart, ref matchIndexEnd);
                }

                if (matchIndexStart > matchIndexEnd)
                {
                    matchIndexStart = -1;
                    matchIndexEnd = -1;
                    matchFound = false;
                }
                else
                {
                    matchFound = true;
                }
            }

            return matchFound;
        }
        #endregion

        #region "Get Value by Index"
        public int GetValueByIndexInt(int index)
        {
            try
            {
                return Convert.ToInt32(GetValueByIndex(index));
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        public double GetValueByIndex(int index)
        {
            try
            {
                if (mDataType == eDataTypeToUse.NoDataPresent)
                {
                    return 0;
                }
                else
                {
                    switch (mDataType)
                    {
                        case eDataTypeToUse.IntegerType:
                            return mDataInt[index];
                        case eDataTypeToUse.SingleType:
                            return mDataSingle[index];
                        case eDataTypeToUse.DoubleType:
                            return mDataDouble[index];
                    }
                }
            }
            catch (Exception ex)
            {
                // index is probably out of range
                // Ignore errors
            }

            return 0;
        }

        public float GetValueByIndexSng(int index)
        {
            try
            {
                return Convert.ToSingle(GetValueByIndex(index));
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
        #endregion

        #region "Get Value by Original Index"
        public int GetValueByOriginalIndexInt(int index)
        {
            try
            {
                return Convert.ToInt32(GetValueByOriginalIndex(index));
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        public double GetValueByOriginalIndex(int indexOriginal)
        {
            int index;

            if (!mPointerArrayIsValid || mDataType == eDataTypeToUse.NoDataPresent)
            {
                return 0;
            }
            else
            {
                try
                {
                    index = Array.IndexOf(mPointerIndices, indexOriginal);
                    if (index >= 0)
                    {
                        switch (mDataType)
                        {
                            case eDataTypeToUse.IntegerType:
                                return mDataInt[mPointerIndices[index]];
                            case eDataTypeToUse.SingleType:
                                return mDataSingle[mPointerIndices[index]];
                            case eDataTypeToUse.DoubleType:
                                return mDataDouble[mPointerIndices[index]];
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
                catch (Exception ex)
                {
                    // Ignore errors
                }
            }

            return 0;
        }

        public float GetValueByOriginalIndexSng(int index)
        {
            try
            {
                return Convert.ToSingle(GetValueByOriginalIndex(index));
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
        #endregion

        private void InitializeLocalVariables()
        {
            mDataType = eDataTypeToUse.NoDataPresent;
            ClearUnusedData();

            mUsePointerIndexArray = true;
            InitializePointerIndexArray(0);
        }

        private void InitializePointerIndexArray(int arrayLength)
        {
            int index;

            if (arrayLength < 0)
                arrayLength = 0;
            mPointerIndices = new int[arrayLength];

            for (index = 0; index <= arrayLength - 1; index++)
                mPointerIndices[index] = index;

            if (arrayLength > 0)
            {
                mPointerArrayIsValid = true;
            }
            else
            {
                mPointerArrayIsValid = false;
            }
        }
    }
}