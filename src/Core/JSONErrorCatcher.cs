// <copyright file="JSONErrorCatcher.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.JSON
{
    using Newtonsoft.Json.Serialization;

    /// <summary>
    /// Class to catch error messages from attempted serialisation / de-serialisation.
    /// </summary>
    public class JSONErrorCatcher
    {
        /// <summary>
        /// Gets or sets errors.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Handle error event.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="e">Error event args.</param>
        public void HandleErrorEvent(object sender, ErrorEventArgs e)
        {
            this.Errors.Add(e.ErrorContext.Error.Message);
            e.ErrorContext.Handled = true;
        }
    }
}
