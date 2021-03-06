﻿using System;
using System.IO;

namespace MASIC.DataOutput
{
    public class clsOutputFileHandles : clsMasicEventNotifier
    {
        public StreamWriter ScanStats { get; set; }
        public StreamWriter SICDataFile { get; set; }
        public System.Xml.XmlTextWriter XMLFileForSICs { get; set; }
        public string MSMethodFilePathBase { get; set; }
        public string MSTuneFilePathBase { get; set; }

        public void CloseScanStats()
        {
            if (ScanStats != null)
            {
                ScanStats.Close();
                ScanStats = null;
            }
        }

        public bool CloseAll()
        {
            try
            {
                CloseScanStats();
                if (SICDataFile != null)
                {
                    SICDataFile.Close();
                    SICDataFile = null;
                }

                if (XMLFileForSICs != null)
                {
                    XMLFileForSICs.Close();
                    XMLFileForSICs = null;
                }

                return true;
            }
            catch (Exception ex)
            {
                ReportError("Error in CloseOutputFileHandles", ex);
                return false;
            }
        }
    }
}
