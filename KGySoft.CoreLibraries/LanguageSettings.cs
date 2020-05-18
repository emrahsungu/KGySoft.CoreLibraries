﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: LanguageSettings.cs
///////////////////////////////////////////////////////////////////////////////
//  Copyright (C) KGy SOFT, 2005-2017 - All Rights Reserved
//
//  You should have received a copy of the LICENSE file at the top-level
//  directory of this distribution. If not, then this file is considered as
//  an illegal copy.
//
//  Unauthorized copying of this file, via any medium is strictly prohibited.
///////////////////////////////////////////////////////////////////////////////

#endregion

#region Usings

using System;
using System.Globalization;
using System.Resources;
#if NETFRAMEWORK
using System.Security; 
#endif
using System.Threading;
using KGySoft.CoreLibraries;
using KGySoft.Resources;
#if NETFRAMEWORK
using Microsoft.Win32; 
#endif

#endregion

namespace KGySoft
{
    /// <summary>
    /// Represents the language settings of the current thread. Use this class also when you want to be notified on
    /// language changes and to control the behavior of those <see cref="DynamicResourceManager"/> instances,
    /// which are configured to use centralized settings.
    /// <br/>See the <strong>Remarks</strong> section for details and an example.
    /// </summary>
    /// <seealso cref="PublicResources"/>
    /// <seealso cref="DynamicResourceManager.UseLanguageSettings"/>
    /// <seealso cref="DynamicResourceManager.UseLanguageSettings"/>
    /// <remarks>
    /// <para>If you use <c>KGySoft.CoreLibraries</c> in a class library, then you do not really need to use this class. Just use the publicly available resources via
    /// the <see cref="PublicResources"/> class and let the consumers of your library adjusting the language settings by this class in their applications.</para>
    /// <para>If you use <c>KGySoft.CoreLibraries</c> or other class libraries dependent on <c>KGySoft.CoreLibraries</c> in an application, then use this class to
    /// set the language of your application and the behavior of centralized resource managers. The <c>KGySoft.CoreLibraries</c> contains one centralized resource manager
    /// for the string resources used in the library. Some of these strings can be publicly accessed via the <see cref="PublicResources"/> members. See also the example below.</para>
    /// </remarks>
    /// <example>
    /// The following example demonstrates how to generate resource files in your application for any language.
    /// <code lang="C#"><![CDATA[
    /// using System;
    /// using System.Globalization;
    /// using KGySoft;
    /// using KGySoft.Resources;
    /// 
    /// class Example
    /// {
    ///     static void Main(string[] args)
    ///     {
    ///         LanguageSettings.DisplayLanguage = CultureInfo.GetCultureInfo("de-DE");
    /// 
    ///         // Even though we set the language above centralized resources still return the built-in compiled resources.
    ///         // Of course, if you use compiled resources in your application with the selected language, then they will be considered.
    ///         Console.WriteLine("Non-localized resource:");
    ///         Console.WriteLine(PublicResources.ArgumentNull);
    ///         Console.WriteLine();
    /// 
    ///         // In an application that uses KGySoft.CoreLibraries you can opt-in to use dynamic resources from .resx files.
    ///         LanguageSettings.DynamicResourceManagersSource = ResourceManagerSources.CompiledAndResX;
    /// 
    ///         // Next line tells that whenever a resource is requested, which does not exist in the resource file of the display
    ///         // language, then its resource file will be dynamically created and/or expanded by the requested resource.
    ///         LanguageSettings.DynamicResourceManagersAutoAppend = AutoAppendOptions.AppendFirstNeutralCulture;
    /// 
    ///         // Actually the default value is AutoAppendOptions.AppendFirstNeutralCulture | AutoAppendOptions.AppendOnLoad,
    ///         // which causes to add not just the requested resource but all of the missing ones to the target resource file.
    ///         // Delete the line above to see the difference.
    /// 
    ///         Console.WriteLine("Localized resource:");
    ///         Console.WriteLine(PublicResources.ArgumentNull);
    ///     }
    /// }
    /// 
    /// // When this example is executed for the first time it produces the following output:
    /// 
    /// // Non-localized resource:
    /// // Value cannot be null.
    /// // 
    /// // Localized resource:
    /// // [T]Value cannot be null.]]></code>
    /// <para>The <c>[T]</c> before a resource indicates that a new resource has been generated, which is not translated yet. In the <c>Resources</c> subfolder of your compiled project
    /// now there must be a new file named <c>KGySoft.CoreLibraries.Messages.de.resx</c> (the exact name depends on the display language and the appending strategy). Its content now must be the following:</para>
    /// <code lang="XML"><![CDATA[
    /// <?xml version="1.0"?>
    /// <root>
    ///   <data name = "General_ArgumentNull">
    ///     <value>[T]Value cannot be null.</value>
    ///   </data>
    /// </root>]]></code>
    /// <para>Search for the <c>[T]</c> values in the generated file to find the untranslated resources and feel free to change them. If you change the resource and execute the example again it will now show the translation you provided.</para>
    /// <note type="tip">To see how to add a dynamic resource manager to your own class library
    /// see the <em>Recommended usage for string resources in a class library</em> section in the description of the <see cref="DynamicResourceManager"/> class.</note>
    /// </example>
    public static class LanguageSettings
    {
        #region Constants

        #region Internal Constants

        internal const AutoAppendOptions AutoAppendDefault = AutoAppendOptions.AppendFirstNeutralCulture | AutoAppendOptions.AppendOnLoad;

        #endregion

        #region Private Constants

        private const AutoSaveOptions autoSaveDefault = AutoSaveOptions.LanguageChange | AutoSaveOptions.DomainUnload | AutoSaveOptions.SourceChange;

        #endregion

        #endregion

        #region Fields

#if NETFRAMEWORK
        private static bool captureSystemLocaleChange;
#endif
        private static ResourceManagerSources dynamicResourceManagersSource = ResourceManagerSources.CompiledOnly;
        private static AutoSaveOptions dynamicResourceManagersAutoSave = autoSaveDefault;
        private static AutoAppendOptions dynamicResourceManagersAutoAppend = AutoAppendDefault;
        private static string unknownResourcePrefix = "[U]";
        private static string untranslatedResourcePrefix = "[T]";

        #endregion

        #region Events

        #region Public Events

        /// <summary>
        /// Occurs when the formatting language (<see cref="Thread.CurrentCulture">Thread.CurrentThread.CurrentCulture</see>) has been changed by setting the
        /// <see cref="FormattingLanguage"/> property. Only the subscriptions from the same thread are invoked.
        /// </summary>
        /// <remarks>
        /// Use this event if you want to be notified of changing the formatting culture of the current thread.
        /// When this event is triggered only the subscribers from the thread of the language change are notified.
        /// </remarks>
        /// <seealso cref="FormattingLanguage"/>
        /// <seealso cref="FormattingLanguageChangedGlobal"/>
        [field: ThreadStatic]
        public static event EventHandler FormattingLanguageChanged;

        /// <summary>
        /// Occurs when the formatting language (<see cref="Thread.CurrentCulture">Thread.CurrentThread.CurrentCulture</see>) has been changed in any <see cref="Thread"/>
        /// by setting <see cref="FormattingLanguage"/> property. The subscribers are invoked from all threads.
        /// </summary>
        /// <remarks>
        /// The <see cref="FormattingLanguage"/> property reflects the formatting culture of the current thread (<see cref="Thread.CurrentCulture">Thread.CurrentThread.CurrentCulture</see>).
        /// This event triggers all subscribers regardless of their source thread in the current <see cref="AppDomain"/>.
        /// When the event is triggered, the subscribers are invoked in the thread of the changed formatting language. You might
        /// want to check if the thread of the invocation is the same as the thread of the subscription.
        /// To notify subscribers from the affected thread only use the <see cref="FormattingLanguageChanged"/> event instead.
        /// </remarks>
        /// <seealso cref="FormattingLanguage"/>
        /// <seealso cref="FormattingLanguageChanged"/>
        public static event EventHandler FormattingLanguageChangedGlobal;

        /// <summary>
        /// Occurs when the display language (<see cref="Thread.CurrentUICulture">Thread.CurrentThread.CurrentUICulture</see>) has been changed by setting the
        /// <see cref="DisplayLanguage"/> property. Only the subscriptions from the same thread are invoked.
        /// </summary>
        /// <remarks>
        /// Use this event if you want to be notified of changing the display culture of the current thread.
        /// When this event is triggered only the subscribers from the thread of the language change are notified.
        /// </remarks>
        /// <seealso cref="DisplayLanguage"/>
        /// <seealso cref="DisplayLanguageChangedGlobal"/>
        [field: ThreadStatic]
        public static event EventHandler DisplayLanguageChanged;

        /// <summary>
        /// Occurs when the display language (<see cref="Thread.CurrentUICulture">Thread.CurrentThread.CurrentUICulture</see>) has been changed in any <see cref="Thread"/>
        /// by setting <see cref="DisplayLanguage"/> property. The subscribers are invoked from all threads.
        /// </summary>
        /// <remarks>
        /// The <see cref="DisplayLanguage"/> property reflects the display culture of the current thread (<see cref="Thread.CurrentUICulture">Thread.CurrentThread.CurrentUICulture</see>).
        /// This event triggers all subscribers regardless of their source thread in the current <see cref="AppDomain"/>.
        /// When the event is triggered, the subscribers are invoked in the thread of the changed display language. You might
        /// want to check if the thread of the invocation is the same as the thread of the subscription.
        /// To notify subscribers from the affected thread only use the <see cref="DisplayLanguageChanged"/> event instead.
        /// </remarks>
        /// <seealso cref="DisplayLanguage"/>
        /// <seealso cref="DisplayLanguageChanged"/>
        public static event EventHandler DisplayLanguageChangedGlobal;

        #endregion

        #region Internal Events

        internal static event EventHandler DynamicResourceManagersSourceChanged;

        internal static event EventHandler DynamicResourceManagersAutoSaveChanged;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the formatting language of the current <see cref="Thread"/>, which is used for formatting and parsing numbers,
        /// date and time values, currency, etc.
        /// When set, <see cref="FormattingLanguageChanged"/> and <see cref="FormattingLanguageChangedGlobal"/> events are triggered.
        /// </summary>
        /// <remarks>
        /// <para>Formatting language represents the regional setting of formatting and parsing numbers, date and time values,
        /// currency, etc.</para>
        /// <para>Use this property instead of <see cref="Thread.CurrentCulture">Thread.CurrentThread.CurrentCulture</see> to
        /// keep language changes synchronized in your application.</para>
        /// <para>When this property is set, <see cref="FormattingLanguageChanged"/> and <see cref="FormattingLanguageChangedGlobal"/> events are triggered,
        /// which makes possible for example refreshing UI components displaying culture-specific formatted values.</para>
        /// </remarks>
        /// <value>The formatting language of the current <see cref="Thread"/>. By default equals to the language of formats
        /// of system regional settings.</value>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see kangword="null"/>.</exception>
        public static CultureInfo FormattingLanguage
        {
            get => Thread.CurrentThread.CurrentCulture;
            set
            {
                CultureInfo orig = Thread.CurrentThread.CurrentCulture;
                if (ReferenceEquals(orig, value))
                    return;

                Thread.CurrentThread.CurrentCulture = value;
                OnFormattingLanguageChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets or sets the display language of the current <see cref="Thread"/>, which is used for looking up localizable resources.
        /// When set, <see cref="DisplayLanguageChanged"/> and <see cref="DisplayLanguageChangedGlobal"/> events are triggered.
        /// <br/>Default value: The display culture of the operating system.
        /// </summary>
        /// <remarks>
        /// <para>Display language represents the language of the user interface of the application. This value is used when
        /// looking up localizable resources.</para>
        /// <para>Use this property instead of <see cref="Thread.CurrentUICulture">Thread.CurrentThread.CurrentUICulture</see> to
        /// keep language changes synchronized in your application.</para>
        /// <para>When this property is set, <see cref="DisplayLanguageChanged"/> and <see cref="DisplayLanguageChangedGlobal"/> events are triggered,
        /// which makes possible for example to refresh the language of the UI on the fly on language change.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public static CultureInfo DisplayLanguage
        {
            get => Thread.CurrentThread.CurrentUICulture;
            set
            {
                CultureInfo orig = Thread.CurrentThread.CurrentUICulture;
                if (ReferenceEquals(orig, value))
                    return;

                Thread.CurrentThread.CurrentUICulture = value;
                OnDisplayLanguageChanged(EventArgs.Empty);
            }
        }

#if !(NETCOREAPP2_0 || NETCOREAPP3_0 || NETSTANDARD2_0 || NETSTANDARD2_1)
        /// <summary>
        /// Gets or sets whether changes of system regional settings should be captured.
        /// When <see langword="true"/>, <see cref="FormattingLanguage"/> is updated on regional changes, and
        /// <see cref="FormattingLanguageChanged"/> and <see cref="FormattingLanguageChangedGlobal"/> events
        /// are triggered.
        /// <br/>Default value: <see langword="false"/>.
        /// </summary>
        /// <value>
        /// <see langword="true"/>&#160;if system regional settings should be captured; otherwise, <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// <note>This property is not available in the .NET Core and .NET Standard packages.
        /// However, if you use .NET Core 2.1 or newer you can reference the <c>Microsoft.Win32.SystemEvents</c> package
        /// to use <see cref="SystemEvents.UserPreferenceChanged"/> event.</note>
        /// </remarks>
        public static bool CaptureSystemLocaleChange
        {
            get => captureSystemLocaleChange;

            [SecuritySafeCritical]
            set
            {
                if (captureSystemLocaleChange == value)
                    return;

                captureSystemLocaleChange = value;
                if (value)
                    SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
                else
                    SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            }
        }
#endif

        /// <summary>
        /// Gets or sets the source, from which the <see cref="DynamicResourceManager"/> instances of the
        /// current application domain should take the resources when their
        /// <see cref="DynamicResourceManager.UseLanguageSettings"/> is <see langword="true"/>.
        /// <br/>Default value: <see cref="ResourceManagerSources.CompiledOnly"/>
        /// </summary>
        /// <remarks>Considering that the default value is <see cref="ResourceManagerSources.CompiledOnly"/>, all <see cref="DynamicResourceManager"/> instances, which
        /// use <see cref="DynamicResourceManager.UseLanguageSettings"/> property with <see langword="true"/>&#160;value, will work fully compatible with the <see cref="ResourceManager"/>
        /// class by default. Therefore, an application, which uses <see cref="DynamicResourceManager"/> instances with centralized settings (maybe indirectly via
        /// class libraries), must opt-in the dynamic behavior of creating .resx resource files on the fly by setting this property either to
        /// <see cref="ResourceManagerSources.CompiledAndResX"/> or <see cref="ResourceManagerSources.ResXOnly"/>.</remarks>
        /// <seealso cref="DynamicResourceManager.UseLanguageSettings"/>
        /// <seealso cref="DynamicResourceManager.Source"/>
        public static ResourceManagerSources DynamicResourceManagersSource
        {
            get => dynamicResourceManagersSource;
            set
            {
                if (value == dynamicResourceManagersSource)
                    return;

                if (!value.IsDefined())
                    Throw.EnumArgumentOutOfRangeWithValues(Argument.value, value);

                dynamicResourceManagersSource = value;
                OnDynamicResourceManagersSourceChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets or sets the auto saving options for the <see cref="DynamicResourceManager"/> instances
        /// of the current application domain when their <see cref="DynamicResourceManager.UseLanguageSettings"/> is <see langword="true"/>.
        /// <br/>Default value: <see cref="AutoSaveOptions.LanguageChange"/>, <see cref="AutoSaveOptions.DomainUnload"/>, <see cref="AutoSaveOptions.SourceChange"/>
        /// </summary>
        /// <remarks>Considering that the default value of the <see cref="DynamicResourceManagersSource"/> property is <see cref="ResourceManagerSources.CompiledOnly"/>,
        /// none of the <see cref="DynamicResourceManager"/>, whose <see cref="DynamicResourceManager.UseLanguageSettings"/> property is <see langword="true"/>&#160;will
        /// auto save their content by default. Therefore, an application, which uses <see cref="DynamicResourceManager"/> instances with centralized settings (maybe indirectly via
        /// class libraries), must opt-in the dynamic behavior of creating .resx resource files on the fly by setting the <see cref="DynamicResourceManagersSource"/> property
        /// either to <see cref="ResourceManagerSources.CompiledAndResX"/> or <see cref="ResourceManagerSources.ResXOnly"/>.</remarks>
        /// <seealso cref="DynamicResourceManager.UseLanguageSettings"/>
        /// <seealso cref="DynamicResourceManager.AutoSave"/>
        /// <seealso cref="DynamicResourceManager.AutoSaveError"/>
        public static AutoSaveOptions DynamicResourceManagersAutoSave
        {
            get => dynamicResourceManagersAutoSave;
            set
            {
                if (value == dynamicResourceManagersAutoSave)
                    return;

                if (!value.AllFlagsDefined())
                    Throw.FlagsEnumArgumentOutOfRange(Argument.value, value);

                dynamicResourceManagersAutoSave = value;
                OnDynamicResourceManagersAutoSaveChanged(EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets or sets the auto append options for the <see cref="DynamicResourceManager"/> instances
        /// of the current application domain when their <see cref="DynamicResourceManager.UseLanguageSettings"/> is <see langword="true"/>.
        /// <br/>Default value: <see cref="AutoAppendOptions.AppendFirstNeutralCulture"/>, <see cref="AutoAppendOptions.AppendOnLoad"/>
        /// </summary>
        /// <remarks>Considering that the default value of the <see cref="DynamicResourceManagersSource"/> property is <see cref="ResourceManagerSources.CompiledOnly"/>,
        /// none of the <see cref="DynamicResourceManager"/>, whose <see cref="DynamicResourceManager.UseLanguageSettings"/> property is <see langword="true"/>&#160;will
        /// auto append their content by default. Therefore, an application, which uses <see cref="DynamicResourceManager"/> instances with centralized settings (maybe indirectly via
        /// class libraries), must opt-in the dynamic behavior of creating .resx resource files on the fly by setting the <see cref="DynamicResourceManagersSource"/> property
        /// either to <see cref="ResourceManagerSources.CompiledAndResX"/> or <see cref="ResourceManagerSources.ResXOnly"/>.</remarks>
        /// <seealso cref="DynamicResourceManager.UseLanguageSettings"/>
        /// <seealso cref="DynamicResourceManager.AutoAppend"/>
        public static AutoAppendOptions DynamicResourceManagersAutoAppend
        {
            get => dynamicResourceManagersAutoAppend;
            set
            {
                if (value == dynamicResourceManagersAutoAppend)
                    return;

                value.CheckOptions();
                dynamicResourceManagersAutoAppend = value;
            }
        }

        /// <summary>
        /// Gets or sets the prefix of an untranslated <see cref="string"/> resource.
        /// Used by the <see cref="DynamicResourceManager"/> instances if <see cref="DynamicResourceManagersAutoAppend"/>
        /// or <see cref="DynamicResourceManager.AutoAppend"/> property is configured to use auto appending.
        /// <br/>Default value: <c>[T]</c>
        /// </summary>
        public static string UntranslatedResourcePrefix
        {
            get => untranslatedResourcePrefix;
            set => untranslatedResourcePrefix = value ?? Throw.ArgumentNullException<string>(Argument.value);
        }

        /// <summary>
        /// Gets or sets the prefix of an unknown (non-existing) <see cref="string"/> resource.
        /// Used by the <see cref="DynamicResourceManager"/> instances if <see cref="DynamicResourceManagersAutoAppend"/>
        /// or <see cref="DynamicResourceManager.AutoAppend"/> property is configured to add non existing resources to the invariant resource set.
        /// <br/>Default value: <c>[U]</c>
        /// </summary>
        public static string UnknownResourcePrefix
        {
            get => unknownResourcePrefix;
            set => unknownResourcePrefix = value ?? Throw.ArgumentNullException<string>(Argument.value);
        }

        #endregion

        #region Methods

        #region Private Methods

        private static void OnFormattingLanguageChanged(EventArgs e)
        {
            // raising the global event
            FormattingLanguageChangedGlobal?.Invoke(null, e);

            // raising the local event
            FormattingLanguageChanged?.Invoke(null, e);
        }

        private static void OnDisplayLanguageChanged(EventArgs e)
        {
            // raising the global event
            DisplayLanguageChangedGlobal?.Invoke(null, e);

            // raising the local event
            DisplayLanguageChanged?.Invoke(null, e);
        }
        private static void OnDynamicResourceManagersSourceChanged(EventArgs e) => DynamicResourceManagersSourceChanged?.Invoke(null, e);

        private static void OnDynamicResourceManagersAutoSaveChanged(EventArgs e) => DynamicResourceManagersAutoSaveChanged?.Invoke(null, e);

        #endregion

        #region Event handlers

#if !(NETCOREAPP2_0 || NETCOREAPP3_0 || NETSTANDARD2_0 || NETSTANDARD2_1)
        [SecuritySafeCritical]
        static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.Locale)
                return;

            Thread.CurrentThread.CurrentCulture.ClearCachedData();
            OnFormattingLanguageChanged(EventArgs.Empty);
        }
#endif

        #endregion

        #endregion
    }
}
