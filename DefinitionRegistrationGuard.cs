#nullable enable

using System;

namespace SilverpineMods.QOLNPCSelector;

internal static class DefinitionRegistrationGuard
{
    internal static void EnsureAvailable<T>(T? registered, string name)
        where T : class
    {
        if (registered != null)
        {
            throw new InvalidOperationException(
                "Definition \"" + name + "\" is already loaded. "
                + "Its existing runtime registration was left unchanged. "
                + "Restart Silverpine to reconcile files moved outside "
                + "the game before enabling this SILC.");
        }
    }

    internal static void Load<T>(
        T definition,
        string name,
        Func<T?> findRegistered,
        Action<T> load,
        Action<T> unload)
        where T : class
    {
        // Outside the try: no cleanup is appropriate if we never loaded.
        EnsureAvailable(findRegistered(), name);
        try
        {
            load(definition);
        }
        catch (Exception loadException)
        {
            try
            {
                UnloadOwned(definition, name, findRegistered, unload);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Loading definition \"" + name
                    + "\" failed, and cleanup could not complete.",
                    loadException,
                    cleanupException);
            }

            throw;
        }
    }

    internal static void UnloadOwned<T>(
        T definition,
        string name,
        Func<T?> findRegistered,
        Action<T> unload)
        where T : class
    {
        T? registered = findRegistered();
        if (registered != null && !ReferenceEquals(registered, definition))
        {
            throw new InvalidOperationException(
                "Refused to unload \"" + name + "\": another definition "
                + "owns its runtime registration. That registration was "
                + "left unchanged.");
        }

        // Player Character Load creates its UI before dictionary.Add. When
        // no registration exists, native Unload must still release that UI.
        unload(definition);
    }
}
