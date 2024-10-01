// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Configuration
{
    internal static class PrivilegedConfigurationManager
    {
        internal static ConnectionStringSettingsCollection ConnectionStrings
        {
            [RequiresUnreferencedCode("Calls System.Configuration.ConfigurationManager.ConnectionStrings")]
            get => ConfigurationManager.ConnectionStrings;
        }

        [RequiresUnreferencedCode("Calls System.Configuration.ConfigurationManager.GetSection(String)")]
        internal static object GetSection(string sectionName)
        {
            return ConfigurationManager.GetSection(sectionName);
        }
    }
}
