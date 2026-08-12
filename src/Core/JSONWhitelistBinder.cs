// <copyright file="JSONWhitelistBinder.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.JSON
{ 
    using Newtonsoft.Json.Serialization;

    /// <summary>
    /// Binder to whitelist types where a statement on the type must be included in the serialised file.
    /// For example, a collection of an interface will need to tell the serialised file what specific implementation it is.
    /// The whitelist is required because if any type is allowed there are some types that can be used maliciously if creation is attempted.
    /// Additionally, this way the whitelisted types can be given a more human readable name.
    /// </summary>
    public class JSONWhitelistBinder : ISerializationBinder
    {
        /// <summary>
        /// Gets binder names.
        /// </summary>
        public static Dictionary<Type, string> DisplayNames { get; } = new Dictionary<Type, string>()
        {
            // Priors
            //{ typeof(GaussianBoxCoxPrior), nameof(GaussianBoxCoxPrior) },
            //{ typeof(UniformPrior), nameof(UniformPrior) },
        };

        /// <summary>
        /// Bind to name.
        /// </summary>
        /// <param name="serializedType">Type.</param>
        /// <param name="assemblyName">Assembly name.</param>
        /// <param name="typeName">Type name.</param>
        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            // Assembly name not used.
            assemblyName = null;
            typeName = null;
            if (DisplayNames.ContainsKey(serializedType))
            {
                typeName = DisplayNames[serializedType];
            }
        }

        /// <summary>
        /// Bind to type.
        /// </summary>
        /// <param name="assemblyName">Assembly name.</param>
        /// <param name="typeName">Type name.</param>
        /// <returns>Type.</returns>
        public Type BindToType(string assemblyName, string typeName)
        {
            // Assembly name not used.
            KeyValuePair<Type, string> typeAndName = DisplayNames.SingleOrDefault(t => t.Value == typeName);
            return typeAndName.Key;
        }
    }
}
