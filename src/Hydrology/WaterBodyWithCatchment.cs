// <copyright file="WaterBodyWithCatchment.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using OSGeo.OGR;
    using RODIS.Static;
    using System.Data;
    using UnitsNet;
    using UnitsNet.Units;

    public class WaterBodyWithCatchment
    {
        /// <summary>Unique string identifier for this water body, read from spatial input.</summary>
        public string Identifier { get; set; } = string.Empty;

        /// <summary>Display label for this water body.</summary>
        public string Label { get; set; }

        /// <summary>Descriptive comment or note for this water body.</summary>
        public string Comment { get; set; }

        /// <summary>Water body type classification string (e.g. "Farm Dam", "Turkey Nest").</summary>
        public string Type { get; set; }

        /// <summary>Surface area of the water body at full supply level (m²).</summary>
        public double SurfaceAream2 { get; set; } = 0.0;

        /// <summary>Local catchment area draining to this water body (km²).</summary>
        public double CatchmentAreakm2 { get; set; } = 0.0;

        /// <summary>Total upstream catchment area including this node's local catchment (km²).</summary>
        public double TotalCatchmentAreakm2 { get; set; } = 0.0;

        /// <summary>Storage volume at full supply level (ML).</summary>
        public double VolumeML { get; set; } = 0.0;

        /// <summary>Object ID from a previous mapping revision, for traceability.</summary>
        public string PreviousObjectID { get; set; } = "-9999";

        /// <summary>Catchment ID of the next downstream water body's catchment polygon.</summary>
        public string NextDownstreamCatchmentID { get; set; } = string.Empty;

        /// <summary>Catchment polygon ID for this water body.</summary>
        public string CatchmentID { get; set; } = string.Empty;

        /// <summary>Array index of the next downstream water body. Set during network topology resolution.</summary>
        public int NextDownstreamArrayPosition { get; set; } = -99;

        /// <summary>True if a catchment polygon has been matched to this water body.</summary>
        public bool doesHaveCatchmentAssigned { get; set; } = false;

        /// <summary>Demand group label for this water body.</summary>
        public string DemandGroup { get; set; } = string.Empty;

        /// <summary>Results/reporting group label for this water body.</summary>
        public string ResultsGroup { get; set; } = string.Empty;

        /// <summary>Bypass flow rate capacity in ML/d. Zero = no bypass.</summary>
        public double BypassFlowRate { get; set; } = 0.0;

        /// <summary>Water body type group properties, if assigned from dam type classification.</summary>
        public WaterBodyType WaterBodyType { get; set; } = null;

        /// <summary>Start date of this water body's existence.</summary>
        public DateTime StartDate { get; set; } = DateTime.MinValue;

        /// <summary>End date of this water body's existence.</summary>
        public DateTime EndDate { get; set; } = DateTime.MaxValue;

        /// <summary>Start date from which bypass is active.</summary>
        public DateTime StartBypassDate { get; set; } = DateTime.MinValue;

        /// <summary>End date after which bypass is no longer active.</summary>
        public DateTime EndBypassDate { get; set; } = DateTime.MaxValue;

        /// <summary>Pumped inflow capacity in ML/d. Zero = no pumped inflow.</summary>
        public double PumpedInflowCapacity { get; set; } = 0.0;

        /// <summary>Start date from which pumped inflow is active.</summary>
        public DateTime StartPumpedInflowDate { get; set; } = DateTime.MinValue;

        /// <summary>End date after which pumped inflow is no longer active.</summary>
        public DateTime EndPumpedInflowDate { get; set; } = DateTime.MaxValue;

        /// <summary>Source dataset identifier (e.g. "2015 Mapping", "2020 Revision").</summary>
        public string Source { get; set; }

        /// <summary>True if this water body passes the limitation/filter check. False = excluded from model.</summary>
        public bool DoesComplyWithLimitationCheck { get; set; } = true;

        /// <summary>Whether this water body is on a waterway: "y" or "n".</summary>
        public string OnWaterway { get; set; } = "No";

        /// <summary>True if this node represents the catchment outlet (no downstream connection).</summary>
        public bool IsOutlet { get; set; } = false;

        /// <summary>True if this node has a physical water body (non-zero volume and area). False for catchment-only outlets.</summary>
        public bool IsWaterbody { get; set; } = true;

        /// <summary>Gets or sets the catchment centroid easting (m, projected coordinate system).</summary>
        public double Easting { get; set; }

        /// <summary>Gets or sets the catchment centroid northing (m, projected coordinate system).</summary>
        public double Northing { get; set; }

        /// <summary>Gets or sets the representative elevation of this catchment (m AHD), typically the centroid or mean elevation from a DEM.</summary>
        public double Elevation { get; set; }

        /// <summary>
        /// Get an array of indices for the field names in the input spatial layer
        /// </summary>
        /// <param name="layer">Spatial layer</param>
        /// <param name="fieldNames">Array of names of fields to get indices for</param>
        /// <returns></returns>
        static private int[] GetFieldIndices(Layer layer, string[] fieldNames)
        {
            int[] fieldIndicesToGet = new int[fieldNames.Length];

            for (int iField = 0; iField < fieldNames.Length; iField++)
            {
                fieldIndicesToGet[iField] = layer.GetLayerDefn().GetFieldIndex(fieldNames[iField]);
            }

            return fieldIndicesToGet;
        }

        /// <summary>
        /// Reads a list of water body information from spatial data file of the water bodies.
        /// </summary>
        /// <param name="shpWaterBodiesPath">Path to shape file containing the water bodies.</param>
        /// <param name="fieldsToGet">Dictionary of fields on water bodies to read from the shape file metadata.</param>
        /// <returns>List of water bodies.</returns>
        static public List<WaterBodyWithCatchment> ReadWaterBodiesFromSpatialDataFile(string shpWaterBodiesPath, Dictionary<string, string> fieldsToGet, string layerName = null,
            int damRevisionMonth = 1, int damRevisionDayOfMonth = 1)
        {
            List<WaterBodyWithCatchment> waterBodies = new List<WaterBodyWithCatchment>();

            using (DataSource waterBodiesDataSource = Ogr.Open(shpWaterBodiesPath, 0))
            {
                if (waterBodiesDataSource == null)
                    throw new InvalidOperationException($"Could not open datasource: {shpWaterBodiesPath}");

                Layer layer = string.IsNullOrEmpty(layerName) ? waterBodiesDataSource.GetLayerByIndex(0) : waterBodiesDataSource.GetLayerByName(layerName);
                if (layer == null)
                    throw new InvalidOperationException($"Layer not found: {layerName ?? "(first layer)"}");

                FeatureDefn defn = layer.GetLayerDefn();
                Layer waterBodiesLayer = waterBodiesDataSource.GetLayerByIndex(0);

                string[] fieldKeysArray = fieldsToGet.Keys.ToArray();
                string[] fieldsToGetArray = fieldsToGet.Values.ToArray();

                AreaUnit inputSurfAreaUnit = AreaUnit.SquareMeter;
                VolumeUnit inputVolumeUnit = VolumeUnit.Megaliter;

                for (int iField = 0; iField < fieldsToGet.Count; ++iField)
                {
                    switch (fieldKeysArray[iField].Trim().ToLower())
                    {
                        case "volumeunits":
                            if (Volume.TryParse("1 " + fieldsToGetArray[iField].Trim(), out var volumeUnits))
                            {
                                inputVolumeUnit = volumeUnits.Unit;
                            }
                            break;
                        case "surfaceareaunits":
                            if (Area.TryParse("1 " + fieldsToGetArray[iField].Trim(), out var saUnits))
                            {
                                inputSurfAreaUnit = saUnits.Unit;
                            }
                            break;
                    }
                }

                int[] fieldIndicesToGet = GetFieldIndices(waterBodiesLayer, fieldsToGetArray);

                IEnumerable<int> featList = Enumerable.Range(0, (int)waterBodiesLayer.GetFeatureCount(1));
                int numWaterBodies = featList.Count();

                Feature waterBodyFeature = waterBodiesLayer.GetNextFeature();
                GdalDateTimeReader dtReader = new GdalDateTimeReader();

                HashSet<string> identifierStrings = new HashSet<string>();

                while (waterBodyFeature != null)
                {
                    WaterBodyWithCatchment waterBodyToAdd = new WaterBodyWithCatchment();
                    waterBodyToAdd.Identifier = string.Empty;

                    for (int iField = 0; iField < fieldIndicesToGet.Length; ++iField)
                    {
                        if (fieldIndicesToGet[iField] >= 0)
                        {
                            FieldDefn fieldDef = defn.GetFieldDefn(fieldIndicesToGet[iField]);
                            OSGeo.OGR.FieldType fieldType = fieldDef.GetFieldType();

                            string fieldAsString = waterBodyFeature.GetFieldAsString(fieldIndicesToGet[iField]).Trim();

                            switch (fieldKeysArray[iField].Trim())
                            {
                                case "Identifier":
                                    waterBodyToAdd.Identifier = fieldAsString;
                                    break;
                                //case "Previous Object ID":
                                //    waterBodyToAdd.PreviousObjectID = fieldAsString;
                                //    break;
                                case "Label":
                                    waterBodyToAdd.Label = fieldAsString;
                                    break;
                                case "Comment":
                                    waterBodyToAdd.Comment = fieldAsString;
                                    break;
                                case "Type":
                                    waterBodyToAdd.Type = fieldAsString;
                                    break;
                                case "Source":
                                    waterBodyToAdd.Source = fieldAsString;
                                    break;
                                case "SurfaceAream2":
                                    waterBodyToAdd.SurfaceAream2 = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    break;
                                case "VolumeML":
                                    waterBodyToAdd.VolumeML = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    break;
                                case "SurfaceArea":
                                    double surfaceAreaInput = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    Area convertedSurfArea = new Area(surfaceAreaInput, inputSurfAreaUnit);
                                    waterBodyToAdd.SurfaceAream2 = convertedSurfArea.SquareMeters;
                                    break;
                                case "Volume":
                                    double volumeInput = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    Volume convertedVolume = new Volume(volumeInput, inputVolumeUnit);
                                    waterBodyToAdd.VolumeML = convertedVolume.Megaliters;
                                    break;
                                case "StartDate":
                                    DateTime? tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue)
                                    {
                                        DateTime adopted = tryDateTime.Value;
                                        if (adopted.DayOfYear == 1)
                                        {
                                            adopted = new DateTime(adopted.Year, damRevisionMonth, damRevisionDayOfMonth);
                                        }
                                        waterBodyToAdd.StartDate = adopted;
                                    }
                                    break;
                                case "EndDate":
                                    tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue) waterBodyToAdd.EndDate = tryDateTime.Value;
                                    break;
                                case "BypassFlowRate":
                                    waterBodyToAdd.BypassFlowRate = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    break;
                                case "BypassStartDate":
                                    tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue) waterBodyToAdd.StartBypassDate = tryDateTime.Value;
                                    break;
                                case "BypassEndDate":
                                    tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue) waterBodyToAdd.EndBypassDate = tryDateTime.Value;
                                    break;
                                case "PumpedInflowCapacity":
                                    waterBodyToAdd.PumpedInflowCapacity = waterBodyFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                    break;
                                case "StartPumpedInflowDate":
                                    tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue) waterBodyToAdd.StartPumpedInflowDate = tryDateTime.Value;
                                    break;
                                case "EndPumpedInflowDate":
                                    tryDateTime = dtReader.ReadFromFeature(fieldIndicesToGet[iField], fieldType, waterBodyFeature);
                                    if (tryDateTime.HasValue) waterBodyToAdd.EndPumpedInflowDate = tryDateTime.Value;
                                    break;
                                case "OnWaterway":
                                    waterBodyToAdd.OnWaterway = "n";
                                    if (fieldAsString.ToLower()[0] == 'y' || fieldAsString.ToLower()[0] == 't')
                                    {
                                        waterBodyToAdd.OnWaterway = "y";
                                    }
                                    break;
                                case "ResultsGroup":
                                    waterBodyToAdd.ResultsGroup = fieldAsString;
                                    break;
                                case "DemandGroup":
                                    waterBodyToAdd.DemandGroup = fieldAsString;
                                    break;
                                case "IsWaterbody":
                                    waterBodyToAdd.IsWaterbody = fieldAsString.Trim() == "1";
                                    break;
                            }
                        }
                    }

                    // Water body to add must have a valid string name as the object ID identifier
                    if (!string.IsNullOrEmpty(waterBodyToAdd.Identifier))
                    {
                        if (identifierStrings.Contains(waterBodyToAdd.Identifier))
                        {
                            Console.WriteLine("WARNING: More than one water body object has the same identifier string. Duplicate string " + waterBodyToAdd.Identifier);
                        }
                        else
                        {
                            // TODO: Check what might need to happen with the WaterBodyType element within the waterBodyToAdd
                            if (waterBodyToAdd.IsWaterbody)
                            {
                                waterBodies.Add(waterBodyToAdd);
                            }

                            identifierStrings.Add(waterBodyToAdd.Identifier);
                        }
                    }

                    waterBodyFeature = waterBodiesLayer.GetNextFeature();
                }

                waterBodiesLayer.Dispose();
                waterBodiesDataSource.Dispose();
            }

            return waterBodies;
        }

        /// <summary>
        /// Adds catchment data to the list of water bodies.
        /// </summary>
        /// <param name="allWaterBodiesList">List of all water bodies and their catchments. Adds a catchment to the outlet that may not have a water body.</param>
        /// <param name="shpFarmDamCatchmentsPath">Path to shape file containing catchments.</param>
        /// <param name="fieldsToAdd">Dictionary specifying the fields to add from the catchment shape file to the water body with catchments list.</param>
        /// <param name="WaterBodyKeyFieldName">Key field name for the water body, default is WaterBodyID.</param>
        /// <returns>Number of catchments added. Should be a positive integer (0 or negative value is an error).</returns>
        static public int AddCatchmentDataToWaterBodies(
            List<WaterBodyWithCatchment> allWaterBodiesList,
            string shpFarmDamCatchmentsPath,
            Dictionary<string, string> fieldsToAdd,
            string WaterBodyKeyFieldName = "WaterBodyID")
        {
            int numCatchmentsAdded = 0;

            DataSource catchmentsDataSource = Ogr.Open(shpFarmDamCatchmentsPath, 0);

            Layer catchmentsLayer = catchmentsDataSource.GetLayerByIndex(0);

            string[] fieldKeysArray = fieldsToAdd.Keys.ToArray();
            string[] fieldsToGetArray = fieldsToAdd.Values.ToArray();

            int[] fieldIndicesToGet = GetFieldIndices(catchmentsLayer, fieldsToGetArray);

            int fieldPosition = fieldKeysArray.ToList().IndexOf(WaterBodyKeyFieldName);

            if (fieldPosition == -1)
            {
                // Error - can't find specified water body ID field in identifier list
                return -1;
            }
            else
            {
                string keyFieldNameInCatchments = fieldsToAdd[WaterBodyKeyFieldName];
                int keyFieldIndex = catchmentsLayer.GetLayerDefn().GetFieldIndex(keyFieldNameInCatchments);

                IEnumerable<int> featList = Enumerable.Range(0, (int)catchmentsLayer.GetFeatureCount(1));
                int numCatchments = featList.Count();

                List<int> matchIndexList = new List<int>();

                Feature catchmentFeature = catchmentsLayer.GetNextFeature();

                while (catchmentFeature != null)
                {
                    string fieldAsString = catchmentFeature.GetFieldAsString(keyFieldIndex).Trim();

                    int iMatched = -1;
                    for (int i = 0; i < allWaterBodiesList.Count() && iMatched < 0; i++)
                    {
                        if (fieldAsString == allWaterBodiesList[i].Identifier)
                        {
                            iMatched = i;
                        }
                    }

                    if (iMatched < 0)
                    {
                        // Add a new water body with no volume and area to represent this additional catchment
                        WaterBodyWithCatchment catchmentWithoutWaterBody = new WaterBodyWithCatchment()
                        {
                            SurfaceAream2 = 0.0,
                            VolumeML = 0.0,
                            BypassFlowRate = 0.0,
                            PumpedInflowCapacity = 0.0,
                            Identifier = catchmentFeature.GetFieldAsString(keyFieldIndex).Trim(),
                            StartDate = DateTime.MinValue,
                            EndDate = DateTime.MaxValue,
                            Comment = "Catchment specified without water body in water bodies shape file",
                            // IsOutlet = true,
                            Label = "CwoWB_" + catchmentFeature.GetFieldAsString(keyFieldIndex).Trim(),
                        };

                        allWaterBodiesList.Add(catchmentWithoutWaterBody);
                        iMatched = allWaterBodiesList.Count() - 1;
                    }

                    matchIndexList.Add(iMatched);

                    catchmentFeature = catchmentsLayer.GetNextFeature();
                }

                catchmentsLayer.ResetReading();
                catchmentFeature = catchmentsLayer.GetNextFeature();

                HashSet<string> catchmentIdentifiers = new HashSet<string>();

                int j = 0;
                while (catchmentFeature != null && j < matchIndexList.Count)
                {
                    if (matchIndexList[j] >= 0 && matchIndexList[j] < allWaterBodiesList.Count())
                    {
                        for (int iField = 0; iField < fieldIndicesToGet.Length; ++iField)
                        {
                            if (fieldIndicesToGet[iField] >= 0)
                            {
                                switch (fieldKeysArray[iField].Trim())
                                {
                                    case "LocalAreakm2":
                                        allWaterBodiesList[matchIndexList[j]].CatchmentAreakm2 = catchmentFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                        allWaterBodiesList[matchIndexList[j]].doesHaveCatchmentAssigned = true;
                                        break;

                                    case "LocalCatchmentID":
                                        allWaterBodiesList[matchIndexList[j]].CatchmentID = catchmentFeature.GetFieldAsString(fieldIndicesToGet[iField]);
                                        if (string.IsNullOrEmpty(allWaterBodiesList[matchIndexList[j]].CatchmentID))
                                        {
                                            Console.WriteLine("WARNING: NULL value for water body catchment identifier");
                                        }
                                        else
                                        {
                                            if (catchmentIdentifiers.Contains(allWaterBodiesList[matchIndexList[j]].CatchmentID))
                                            {
                                                Console.WriteLine("WARNING: More than catchment object has the same identifier string. Duplicate string " + allWaterBodiesList[matchIndexList[j]].CatchmentID);
                                            }
                                            else
                                            {
                                                catchmentIdentifiers.Add(allWaterBodiesList[matchIndexList[j]].CatchmentID);
                                            }
                                        }
                                        break;

                                    case "DownstreamCatchmentID":
                                        if (!allWaterBodiesList[matchIndexList[j]].IsOutlet)
                                        {
                                            allWaterBodiesList[matchIndexList[j]].NextDownstreamCatchmentID = catchmentFeature.GetFieldAsString(fieldIndicesToGet[iField]);
                                            if (string.IsNullOrEmpty(allWaterBodiesList[matchIndexList[j]].NextDownstreamCatchmentID))
                                            {
                                                Console.WriteLine("WARNING: NULL value for next downstream water body catchment identifier");
                                            }
                                        }
                                        break;

                                    // -- Spatial coordinates (optional, for runoff spatial variation U11 tilt) --
                                    case "Easting":
                                        allWaterBodiesList[matchIndexList[j]].Easting = catchmentFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                        break;
                                    case "Northing":
                                        allWaterBodiesList[matchIndexList[j]].Northing = catchmentFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                        break;
                                    case "Elevation":
                                        allWaterBodiesList[matchIndexList[j]].Elevation = catchmentFeature.GetFieldAsDouble(fieldIndicesToGet[iField]);
                                        break;
                                }
                            }
                        }

                        ++numCatchmentsAdded;
                    }

                    catchmentFeature = catchmentsLayer.GetNextFeature();
                    ++j;
                }

                // Check that catchment ID of next downstream catchment is valid unless the catchment has no water body attached (i.e. it's the catchment outlet)
                int countNumOutlets = 0;

                for (int i = 0; i < allWaterBodiesList.Count; i++)
                {
                    bool isValidDSIdentifier = false;
                    for (int k = 0; k < allWaterBodiesList.Count && !isValidDSIdentifier; k++)
                    {
                        if (i != k)
                        {
                            if (!string.IsNullOrEmpty(allWaterBodiesList[i].NextDownstreamCatchmentID))
                            {
                                if (!string.IsNullOrEmpty(allWaterBodiesList[k].CatchmentID))
                                {
                                    if (allWaterBodiesList[i].NextDownstreamCatchmentID == allWaterBodiesList[k].CatchmentID)
                                    {
                                        isValidDSIdentifier = true;
                                    }
                                }
                            }
                        }
                    }

                    if (!isValidDSIdentifier)
                    {
                        if (countNumOutlets == 0)
                        {
                            Console.WriteLine("NOTE: Catchment with identifier " + allWaterBodiesList[i].Identifier + " identified as catchment outlet.");
                            allWaterBodiesList[i].IsOutlet = true;
                        }
                        else
                        {
                            Console.WriteLine("ERROR: Catchment with identifer " + allWaterBodiesList[i].Identifier + " does not have a validly idenfied downstream catchment identifier.");
                        }

                        ++countNumOutlets;
                    }
                }

                if (countNumOutlets > 1)
                {
                    numCatchmentsAdded = -countNumOutlets;
                }

                catchmentsLayer.Dispose();
                catchmentsDataSource.Dispose();
            }

            return numCatchmentsAdded;
        }

        

        /// <summary>
        /// Applies a limitation to which water bodies are included in the RODIS model, based upon type or date.
        /// </summary>
        /// <param name="allWaterBodies">Array of water bodies, with field DoesComplyWithLimitations updated.</param>
        /// <param name="limitationsSpecification">Dictionary specifying limitations on date or type to be applied.</param>
        public static void CheckLimitationsForWaterBodies(WaterBodyWithCatchment[] allWaterBodies, Dictionary<string, string> limitationsSpecification)
        {
            List<WaterBodyWithCatchment> waterBodiesAfterLimitationsCheck = new List<WaterBodyWithCatchment>();
            List<int> limitedWaterBodiesIndices = new List<int>();

            for (int i = 0; i < allWaterBodies.Length; ++i)
            {
                bool doesComplyWithLimitation = true;

                // Check whether this water body has a non-tiny volume. Water bodies with no volume are probably catchment outlets and should always be included.
                // Also check whether there is any limitations specification provided
                if (allWaterBodies[i].VolumeML > 1e-6 && limitationsSpecification != null)
                {
                    doesComplyWithLimitation = false;

                    foreach (KeyValuePair<string, string> limitation in limitationsSpecification)
                    {
                        switch (limitation.Key)
                        {
                            case "Source":
                                if (allWaterBodies[i].Source == limitation.Value)
                                {
                                    doesComplyWithLimitation = true;
                                }
                                break;

                            case "Date":
                                DateTime limitationDate = DateTime.Now;
                                bool isParseOK = DateTime.TryParse(limitation.Value, out limitationDate);
                                if (isParseOK)
                                {
                                    if (allWaterBodies[i].StartDate < limitationDate && allWaterBodies[i].EndDate >= limitationDate)
                                    {
                                        doesComplyWithLimitation = true;
                                    }
                                }
                                break;
                        }
                    }
                }

                allWaterBodies[i].DoesComplyWithLimitationCheck = doesComplyWithLimitation;
            }
        }

        /// <summary>
        /// Reads an optional double field from the GIS row. Returns the default value if the field name is not in the label dictionary or the value is null/empty.
        /// </summary>
        private static double GetOptionalDoubleField(
            DataRow row, Dictionary<string, string> fieldLabels, string key, double defaultValue)
        {
            if (!fieldLabels.TryGetValue(key, out string fieldName))
                return defaultValue;
            if (string.IsNullOrWhiteSpace(fieldName))
                return defaultValue;
            if (!row.Table.Columns.Contains(fieldName))
                return defaultValue;

            object val = row[fieldName];
            if (val == null || val == DBNull.Value)
                return defaultValue;

            return Convert.ToDouble(val);
        }
    }
}
