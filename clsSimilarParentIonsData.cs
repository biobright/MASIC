﻿using System.Collections.Generic;

namespace MASIC
{
    public class clsSimilarParentIonsData
    {
        public int[] MZPointerArray { get; set; }

        public int IonInUseCount { get; set; }

        public bool[] IonUsed { get; }

        public List<clsUniqueMZListItem> UniqueMZList { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        public clsSimilarParentIonsData(int parentIonCount)
        {
            MZPointerArray = new int[parentIonCount];
            IonUsed = new bool[parentIonCount];

            UniqueMZList = new List<clsUniqueMZListItem>();
        }

        public override string ToString()
        {
            return "IonInUseCount: " + IonInUseCount;
        }
    }
}
