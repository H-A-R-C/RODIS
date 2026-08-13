// <copyright file="JSONSerialisation.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.JSON
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// JSON serialization class.
    /// </summary>
    public static class JSONSerialisation
    {
        /// <summary>
        /// Get serialiser settings.
        /// </summary>
        /// <returns>Settings.</returns>
        public static JsonSerializerSettings GetSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                MissingMemberHandling = MissingMemberHandling.Error,
                SerializationBinder = new JSONWhitelistBinder(),
            };

            return settings;
        }

        /// <summary>
        /// Get serialiser.
        /// </summary>
        /// <returns>Serialiser.</returns>
        public static JsonSerializer GetSerialiser()
        {
            JsonSerializer serialiser = new JsonSerializer()
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                MissingMemberHandling = MissingMemberHandling.Error,
                SerializationBinder = new JSONWhitelistBinder(),
            };

            return serialiser;
        }

        /// <summary>
        /// Serialise object and keep list of errors if any.
        /// </summary>
        /// <typeparam name="T">Type of object to serialise.</typeparam>
        /// <param name="toSerialise">Object to serialise.</param>
        /// <returns>Errors or serialised content.</returns>
        public static (List<string> errors, string jsonContent) Serialise<T>(T toSerialise)
        {
            // Get everything ready for serialisation
            JsonSerializerSettings settings = GetSettings();
            JSONErrorCatcher catcher = new JSONErrorCatcher();
            settings.Error = catcher.HandleErrorEvent;

            // Try to serialise
            string jsonContent = JsonConvert.SerializeObject(toSerialise, settings);

            // Get output ready
            List<string> errors = CopyOrNullIfNoItems(catcher.Errors);
            jsonContent = errors == null ? jsonContent : null;

            return (errors, jsonContent);
        }

        /// <summary>
        /// De-serialise object, preprocess (e.g. to handle obsolete parameters) and keep list of errors if any.
        /// </summary>
        /// <typeparam name="T">Type of object being de-serialised.</typeparam>
        /// <param name="jsonContent">JSON file content.</param>
        /// <param name="preprocessing">Preprocessing to apply to de-serialised <see cref="JObject"/> (e.g. to handle obsolete parameters).</param>
        /// <returns>De-serialised object.</returns>
        public static (List<string> errors, T deserialised) DeserialiseWithPreprocessing<T>(string jsonContent, Action<JObject, List<string>> preprocessing)
            where T : class
        {
            // Get everything ready for serialisation
            JSONErrorCatcher catcher = new JSONErrorCatcher();
            JsonSerializer serialiser = GetSerialiser();
            serialiser.Error += catcher.HandleErrorEvent;

            // Get JObject
            JObject jContent = JObject.Parse(jsonContent);

            // Early exit if could not parse raw file
            if (catcher.Errors.Count != 0)
            {
                serialiser.Error -= catcher.HandleErrorEvent;
                return (catcher.Errors.ToList(), null);
            }

            // Apply preprocessing
            preprocessing.Invoke(jContent, catcher.Errors);

            // Early exit if could not apply preprocessing
            if (catcher.Errors.Count != 0)
            {
                serialiser.Error -= catcher.HandleErrorEvent;
                return (catcher.Errors.ToList(), null);
            }

            // To object
            T content = jContent.ToObject<T>(serialiser);
            serialiser.Error -= catcher.HandleErrorEvent;

            // Get output ready
            List<string> errors = CopyOrNullIfNoItems(catcher.Errors);
            content = errors == null ? content : null;

            return (errors, content);

            // TODO: Line numbers change after preprocessing so error misleading?
        }

        /// <summary>
        /// De-serialise object and keep list of errors if any.
        /// </summary>
        /// <typeparam name="T">Type of object being de-serialised.</typeparam>
        /// <param name="jsonContent">JSON file content.</param>
        /// <returns>De-serialised object.</returns>
        public static (List<string> errors, T deserialised) Deserialise<T>(string jsonContent)
            where T : class
        {
            return DeserialiseWithPreprocessing<T>(jsonContent, (x, y) => { });
        }

        /// <summary>
        /// De-serialise a JSON file.
        /// </summary>
        /// <typeparam name="T">Type of object being de-serialised.</typeparam>
        /// <param name="path">Path to JSON file.</param>
        /// <returns>De-serialised object.</returns>
        public static (List<string> errors, T deserialised) DeserialiseFile<T>(string path)
            where T : class
        {
            return Deserialise<T>(File.ReadAllText(path));
        }

        /// <summary>
        /// De-serialise a JSON file and throw an exception if an error is encountered.
        /// </summary>
        /// <typeparam name="T">Type of object being de-serialised.</typeparam>
        /// <param name="path">Path to JSON file.</param>
        /// <returns>De-serialised object.</returns>
        public static T DeserialiseFileThrowOnError<T>(string path)
            where T : class
        {
            (List<string> errors, T deserialised) = DeserialiseFile<T>(path);
            ThrowOnErrors(errors);
            return deserialised;
        }

        /// <summary>
        /// Serialise a JSON object to file and throw an exception if an error is encountered.
        /// </summary>
        /// <typeparam name="T">Type of object being serialised.</typeparam>
        /// <param name="toSerialise">Object to serialise.</param>
        /// <param name="path">Path to JSON file.</param>
        public static void SerialiseFileThrowOnError<T>(T toSerialise, string path)
            where T : class
        {
            (List<string> errors, string jsonContent) = Serialise(toSerialise);
            ThrowOnErrors(errors);
            File.WriteAllText(path, jsonContent);
        }

        private static List<string> CopyOrNullIfNoItems(List<string> toCopy)
        {
            return toCopy.Count == 0 ? null : toCopy.ToList();
        }

        private static void ThrowOnErrors(List<string> errors)
        {
            if (errors != null)
            {
                throw new ArgumentException(string.Join(Environment.NewLine, errors));
            }
        }
    }
}
