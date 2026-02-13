using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable container that links a modifier with its parse rule.
    /// Tracks registration state and ownership to prevent bugs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note: "Initialized" state (buffers created, events subscribed) is tracked
    /// by BaseModifier.IsInitialized and happens lazily on first Apply().
    /// </para>
    /// <para>
    /// Ownership rules:
    /// - ModRegister tracks its owner UniText
    /// - Registration fails if already registered to different owner
    /// - State resets on deserialization
    /// </para>
    /// </remarks>
    /// <seealso cref="BaseModifier"/>
    /// <seealso cref="IParseRule"/>
    /// <seealso cref="AttributeParser"/>
    [Serializable]
    public sealed class ModRegister
    {
        /// <summary>The modifier instance to apply when the rule matches.</summary>
        [SerializeReference, TypeSelector] private BaseModifier modifier;
        /// <summary>The parse rule that triggers this modifier.</summary>
        [SerializeReference, TypeSelector] private IParseRule rule;

        [NonSerialized] private UniText owner;
        [NonSerialized] private bool isRegistered;

        /// <summary>Gets or sets the modifier, with proper lifecycle management on hot-swap.</summary>
        public BaseModifier Modifier
        {
            get => modifier;
            set => SetModifier(value);
        }

        /// <summary>Gets or sets the parse rule, with proper lifecycle management on hot-swap.</summary>
        public IParseRule Rule
        {
            get => rule;
            set => SetRule(value);
        }

        /// <summary>Gets the UniText that owns this ModRegister.</summary>
        public UniText Owner => owner;

        /// <summary>Returns true if both modifier and rule are assigned.</summary>
        public bool IsValid => modifier != null && rule != null;

        /// <summary>Returns true if registered to a UniText.</summary>
        public bool IsRegistered => isRegistered;

        /// <summary>
        /// Registers this modifier/rule pair with a UniText and its AttributeParser.
        /// </summary>
        /// <param name="uniText">The UniText component.</param>
        /// <param name="parser">The AttributeParser to register with.</param>
        /// <returns>True if registration succeeded, false if invalid, already registered, or owned by different UniText.</returns>
        internal bool Register(UniText uniText, AttributeParser parser)
        {
            if (!IsValid) return false;

            if (isRegistered && owner == uniText) return true;

            if (owner != null && owner != uniText)
            {
                Debug.LogError($"[UniText] Cannot register to {uniText.name}: already owned by {owner.name}. " +
                               "Create separate ModRegister instances for each UniText.");
                return false;
            }

            owner = uniText;
            modifier.SetOwner(uniText);
            parser.Register(rule, modifier);
            isRegistered = true;
            return true;
        }

        /// <summary>
        /// Unregisters this modifier from the AttributeParser and resets state.
        /// </summary>
        /// <param name="parser">The AttributeParser to unregister from.</param>
        internal void Unregister(AttributeParser parser)
        {
            if (!isRegistered) return;

            if (modifier != null && modifier.IsInitialized)
            {
                modifier.Destroy();
            }

            if (parser != null && modifier != null)
            {
                parser.Unregister(modifier);
            }

            isRegistered = false;
            owner = null;
        }

        /// <summary>
        /// Resets state for deserialization. Called when Unity recreates the object.
        /// </summary>
        internal void ResetState()
        {
            owner = null;
            isRegistered = false;
        }

        /// <summary>
        /// Deinitializes the modifier but keeps it registered (for font changes).
        /// </summary>
        internal void DeinitializeModifier()
        {
            if (modifier != null && modifier.IsInitialized)
            {
                modifier.Destroy();
            }
        }

        private void SetModifier(BaseModifier value)
        {
            if (modifier == value) return;

            var wasInitialized = modifier != null && modifier.IsInitialized;
            var wasRegistered = isRegistered;
            var cachedOwner = owner;

            if (wasRegistered && cachedOwner != null)
            {
                cachedOwner.UnregisterModifierFromParser(this);
            }

            modifier = value;

            if (wasRegistered && cachedOwner != null && IsValid)
            {
                cachedOwner.RegisterModifierWithParser(this);

                if (wasInitialized)
                {
                    cachedOwner.SetDirty(UniText.DirtyFlags.Text);
                }
            }
        }

        private void SetRule(IParseRule value)
        {
            if (rule == value) return;

            var wasInitialized = modifier != null && modifier.IsInitialized;
            var wasRegistered = isRegistered;
            var cachedOwner = owner;

            if (wasRegistered && cachedOwner != null)
            {
                cachedOwner.UnregisterModifierFromParser(this);
            }

            rule = value;

            if (wasRegistered && cachedOwner != null && IsValid)
            {
                cachedOwner.RegisterModifierWithParser(this);

                if (wasInitialized)
                {
                    cachedOwner.SetDirty(UniText.DirtyFlags.Text);
                }
            }
        }
    }

}
