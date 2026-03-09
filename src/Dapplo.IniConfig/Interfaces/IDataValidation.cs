// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Dapplo.IniConfig.Interfaces;

/// <summary>
/// Dispatch interface used by the framework to trigger per-property validation.
/// Consumer code should prefer the generic <see cref="IDataValidation{TSelf}"/> overload
/// which allows the validation logic to live directly in the section interface via static virtuals.
/// Implement this on your section class (e.g. in a partial class) to provide per-property errors.
/// </summary>
public interface IDataValidation
{
    /// <summary>
    /// Validates the property identified by <paramref name="propertyName"/> and returns
    /// any error strings. Return an empty enumerable when the property is valid.
    /// </summary>
    IEnumerable<string> ValidateProperty(string propertyName);
}

#if NET7_0_OR_GREATER
/// <summary>
/// Lifecycle interface that integrates with <c>INotifyDataErrorInfo</c> (WPF, Avalonia, WinForms).
/// Implement this on your section interface and override <see cref="ValidateProperty"/> to supply
/// per-property error messages — no separate partial class is needed.
/// </summary>
/// <remarks>
/// The source generator will automatically implement <c>System.ComponentModel.INotifyDataErrorInfo</c>
/// on the generated class when this interface is detected. Validation is re-run whenever a property
/// annotated with <see cref="Attributes.IniValueAttribute.NotifyPropertyChanged"/> changes.
/// </remarks>
/// <typeparam name="TSelf">The section interface itself (CRTP / curiously-recurring template pattern).</typeparam>
/// <example>
/// <code>
/// [IniSection]
/// public interface IMySettings : IIniSection, IDataValidation&lt;IMySettings&gt;
/// {
///     [IniValue(DefaultValue = "8080", NotifyPropertyChanged = true)]
///     int Port { get; set; }
///
///     static IEnumerable&lt;string&gt; ValidateProperty(IMySettings self, string propertyName)
///     {
///         if (propertyName == nameof(Port) &amp;&amp; self.Port is &lt; 1024 or &gt; 65535)
///             yield return "Port must be between 1024 and 65535.";
///     }
/// }
/// </code>
/// </example>
public interface IDataValidation<TSelf> where TSelf : IDataValidation<TSelf>
{
    /// <summary>
    /// Validates the property identified by <paramref name="propertyName"/> and returns
    /// any error strings. Override this static method in your section interface.
    /// The source generator emits a bridge that connects this static virtual method to the
    /// <see cref="IDataValidation"/> dispatch interface and to <c>INotifyDataErrorInfo</c>.
    /// </summary>
    static virtual IEnumerable<string> ValidateProperty(TSelf self, string propertyName)
        => System.Linq.Enumerable.Empty<string>();
}
#endif
