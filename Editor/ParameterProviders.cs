using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Editor-only registry for dynamic enum providers used by <see cref="ParameterFieldAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Register a provider with a key, then reference it in the attribute type string
    /// as <c>"enum:@key"</c>. The editor drawer will call the provider to populate the dropdown.
    /// <example>
    /// <code>
    /// // Register
    /// ParameterProviders.Register("gradients", () => myGradients.Select(g => g.name));
    ///
    /// // Use in attribute
    /// [ParameterField(0, "Name", "enum:@gradients")]
    /// </code>
    /// </example>
    /// </remarks>
    public static class ParameterProviders
    {
        private static readonly Dictionary<string, Func<IEnumerable<string>>> providers = new();

        /// <summary>Registers a dynamic options provider for the given key.</summary>
        public static void Register(string key, Func<IEnumerable<string>> provider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            providers[key] = provider;
        }

        /// <summary>Removes a previously registered provider.</summary>
        public static void Unregister(string key)
        {
            if (key != null) providers.Remove(key);
        }

        /// <summary>Tries to get the current options from a registered provider.</summary>
        public static bool TryGetOptions(string key, out IEnumerable<string> options)
        {
            options = null;
            if (key == null || !providers.TryGetValue(key, out var provider))
                return false;

            options = provider();
            return options != null;
        }
    }

    [InitializeOnLoad]
    static class BuiltInParameterProviders
    {
        static BuiltInParameterProviders()
        {
            ParameterProviders.Register("gradients", () =>
            {
                var gradients = UniTextSettings.Gradients;
                return gradients != null ? gradients.GradientNames : Enumerable.Empty<string>();
            });
        }
    }
}
