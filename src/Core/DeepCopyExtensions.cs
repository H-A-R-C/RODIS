namespace STEDI.JSON
{
    using Newtonsoft.Json;

    /// <summary>Extension methods for creating deep copies of objects via Newtonsoft JSON round-trip.</summary>
    public static class DeepCopyExtensions
    {
        /// <summary>Creates a deep copy of an object by serialising and deserialising via Newtonsoft JSON.</summary>
        /// <param name="obj">Object to copy.</param>
        /// <returns>Independent deep copy, or default if the input is null.</returns>
        public static T DeepCopyViaNewtonsoft<T>(this T obj)
        {
            if (obj is null) return default!;
            var settings = JSONSerialisation.GetSettings();
            var json = JsonConvert.SerializeObject(obj, settings);
            return JsonConvert.DeserializeObject<T>(json, settings)!;
        }
    }
}
