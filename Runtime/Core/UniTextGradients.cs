using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// ScriptableObject containing named gradients for use with &lt;gradient=name&gt; tags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Create via Assets → Create → UniText → Gradients.
    /// Reference in UniTextSettings to make gradients available to all UniText components.
    /// </para>
    /// </remarks>
    /// <seealso cref="GradientModifier"/>
    /// <seealso cref="UniTextSettings"/>
    [CreateAssetMenu(fileName = "UniTextGradients", menuName = "UniText/Gradients")]
    public sealed class UniTextGradients : ScriptableObject
    {
        /// <summary>
        /// A named gradient entry.
        /// </summary>
        [Serializable]
        public struct NamedGradient
        {
            /// <summary>Name used in markup (e.g., "rainbow" for &lt;gradient=rainbow&gt;).</summary>
            public string name;

            /// <summary>The Unity Gradient with color stops.</summary>
            public Gradient gradient;
        }

        [SerializeField]
        [Tooltip("List of named gradients available for <gradient=name> tags.")]
        private StyledList<NamedGradient> gradients = new();

        private Dictionary<string, Gradient> lookup;

        /// <summary>
        /// Attempts to get a gradient by name.
        /// </summary>
        /// <param name="name">The gradient name (case-insensitive).</param>
        /// <param name="gradient">The gradient if found.</param>
        /// <returns>True if the gradient was found.</returns>
        public bool TryGetGradient(string name, out Gradient gradient)
        {
            EnsureLookup();
            return lookup.TryGetValue(name, out gradient);
        }

        /// <summary>
        /// Gets all gradient names.
        /// </summary>
        public IEnumerable<string> GradientNames
        {
            get
            {
                EnsureLookup();
                return lookup.Keys;
            }
        }

        /// <summary>
        /// Gets the number of gradients.
        /// </summary>
        public int Count => gradients.Count;

        private void EnsureLookup()
        {
            if (lookup != null) return;

            lookup = new Dictionary<string, Gradient>(StringComparer.OrdinalIgnoreCase);
            foreach (var ng in gradients)
            {
                if (!string.IsNullOrEmpty(ng.name) && ng.gradient != null)
                    lookup[ng.name] = ng.gradient;
            }
        }

        public void Add(string gradientName, Gradient gradient)
        {
            EnsureLookup();
            gradients.Add(new NamedGradient
            {
                name = gradientName,
                gradient = gradient
            });

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
            
            lookup.Add(gradientName, gradient);
            OnChanged();
        }
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            lookup = null;
            OnChanged();
        }
#endif

        private void OnChanged()
        {
            if (!UniTextSettings.IsNull && UniTextSettings.Gradients == this)
            {
                UniTextSettings.Instance.InvokeChanged();
            }
        }
        
        private void OnEnable()
        {
            lookup = null;
        }
    }
}
