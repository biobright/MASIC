using PSI_Interface.MSData;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MASIC.DataOutput;

using PSI_Interface.MSData;

// xmlReader.InstrumentParams[0].UserParams

/*
 * var listOfUserParamDatas = new List<UserParamData>
{
  new UserParamData
  {
    Name = "SchemaType",
    Value = "TDF",
    DataType = "xsd:string",
    UnitInfo = null
  },
  new UserParamData
  {
    Name = "SchemaVersionMajor",
    Value = "3",
    DataType = "xsd:string",
    UnitInfo = null
  },

  */

namespace MASIC.DataOutput
{
    public class clsMZMLUserParamsTuneFile : clsMasicEventNotifier
    {
        public bool SaveMSTuneFile(
            SimpleMzMLReader xmlReader,
            clsDataOutput dataOutputHandler)
        {
            {
                const char TAB_DELIMITER = '\t';
                var outputFilePath = "?UndefinedFile?";
                // List<InstrumentData>,
                // element.UserParams = new List<UserParamData>
                var flattenedUserParams = new List<SimpleMzMLReader.UserParamData>();
                try{
                    var instrumentParamsBlocks = xmlReader.InstrumentParams;
                    //var flattenedUserParams = new List<SimpleMzMLReader.UserParamData>();
                    foreach (SimpleMzMLReader.InstrumentData instrumentBlock in instrumentParamsBlocks)
                    {
                        //flattenedInstrumentParams.Add(block);
                        foreach (SimpleMzMLReader.UserParamData userParam in instrumentBlock.UserParams)
                        {
                            flattenedUserParams.Add(userParam);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ReportError("Error looking up UserParamData in xmlReader", ex, clsMASIC.eMasicErrorCodes.OutputFileWriteError);
                    return false;
                }

                try
                {

                        outputFilePath = dataOutputHandler.OutputFileHandles.MSTuneFilePathBase + ".txt";

                        using (var writer = new StreamWriter(outputFilePath, false))
                        {
                            writer.WriteLine("Category" + TAB_DELIMITER + "Name" + TAB_DELIMITER + "Value");
                            writer.WriteLine("General"+TAB_DELIMITER+"=== User Params: === ");

                            foreach (SimpleMzMLReader.UserParamData userParam in flattenedUserParams)
                                // As setting.Category \t setting.Name \t setting.Value
                                // new UserParamData
                                // {
                                //     Name = "InstrumentVendor",
                                //     Value = "Bruker",
                                //     DataType = "xsd:string",
                                //     UnitInfo = null
                                // },
                                writer.WriteLine("UserParam" + TAB_DELIMITER + userParam.Name + TAB_DELIMITER + (userParam.Value==""?"null":userParam.Value));
                            writer.WriteLine();
                        }
                }
                catch (Exception ex)
                {
                    ReportError("Error writing the MZML UserParams to Tune file to: " + outputFilePath, ex, clsMASIC.eMasicErrorCodes.OutputFileWriteError);
                    return false;
                }

                return true;
            }
        }

    }
}
