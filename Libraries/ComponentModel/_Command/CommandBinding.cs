﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: CommandBinding.cs
///////////////////////////////////////////////////////////////////////////////
//  Copyright (C) KGy SOFT, 2005-2018 - All Rights Reserved
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

using KGySoft.Collections;
using KGySoft.Libraries;
using KGySoft.Reflection;

#endregion

namespace KGySoft.ComponentModel
{
    internal sealed class CommandBinding : ICommandBinding
    {
        #region Nested types

        #region SubscriptionInfo class

        private abstract class SubscriptionInfo
        {
            #region Fields

            internal CommandBinding Binding;
            internal string EventName;
            internal object Source;
            internal Delegate Delegate;

            #endregion
        }

        #endregion

        #region SubscriptionInfo<TEventArgs> class

        /// <summary>
        /// For providing a matching signature for any event handler.
        /// </summary>
        private sealed class SubscriptionInfo<TEventArgs> : SubscriptionInfo
            where TEventArgs : EventArgs
        {
            #region Methods

            internal void Execute(object sender, TEventArgs e) => Binding.InvokeCommand(new CommandSource<TEventArgs> { Source = Source, TriggeringEvent = EventName, EventArgs = e });

            #endregion
        }

        #endregion

        #region CommandGenericWrapper struct

        private struct CommandGenericWrapper<TEventArgs> : ICommand<TEventArgs> where TEventArgs : EventArgs
        {
            #region Fields

            private readonly ICommand command;

            #endregion

            #region Constructors

            internal CommandGenericWrapper(ICommand command) => this.command = command;

            #endregion

            #region Methods

            void ICommand<TEventArgs>.Execute(ICommandSource<TEventArgs> source, ICommandState state, object target) => command.Execute(source, state, target);
            void ICommand.Execute(ICommandSource source, ICommandState state, object target) => throw new InvalidOperationException();

            #endregion
        }

        #endregion

        #endregion

        #region Fields

        #region Static Fields

        private static readonly IThreadSafeCacheAccessor<Type, Dictionary<string, EventInfo>> eventsCache = new Cache<Type, Dictionary<string, EventInfo>>(t =>
            t.GetEvents(BindingFlags.Public | BindingFlags.Instance).ToDictionary(e => e.Name, e => e)).GetThreadSafeAccessor();

        private static readonly IThreadSafeCacheAccessor<Type, Dictionary<string, PropertyInfo>> properties = new Cache<Type, Dictionary<string, PropertyInfo>>(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p), 256).GetThreadSafeAccessor();

        #endregion

        #region Instance Fields

        private readonly ICommand command;
        private readonly HashSet<object> targets = new HashSet<object>();
        private readonly CommandState state;
        private readonly Dictionary<object, Dictionary<EventInfo, SubscriptionInfo>> sources = new Dictionary<object, Dictionary<EventInfo, SubscriptionInfo>>();
        private readonly CircularList<ICommandStateUpdater> stateUpdaters = new CircularList<ICommandStateUpdater>();

        private bool disposed;

        #endregion

        #endregion

        #region Properties

        public ICommandState State => state;

        #endregion

        #region Constructors

        internal CommandBinding(ICommand command, IDictionary<string, object> initialState)
        {
            this.command = command ?? throw new ArgumentNullException(nameof(command));
            state = initialState is CommandState s ? s : new CommandState(initialState);
            state.PropertyChanged += State_PropertyChanged;
        }

        #endregion

        #region Methods

        #region Static Methods

        private static void DefaultUpdateState(object source, string propertyName, object stateValue)
        {
            Type type = source.GetType();
            if (!properties[type].TryGetValue(propertyName, out PropertyInfo pi) || !pi.PropertyType.CanAcceptValue(stateValue))
                return;
            Reflector.SetProperty(source, pi, stateValue);
        }

        #endregion

        #region Instance Methods

        #region Public Methods

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            state.PropertyChanged -= State_PropertyChanged;

            foreach (IComponent source in sources.Keys.ToArray())
                RemoveSource(source);

            foreach (ICommandStateUpdater stateUpdater in stateUpdaters)
                stateUpdater.Dispose();
            stateUpdaters.Reset();

            targets.Clear();
        }

        public ICommandBinding AddSource(object source, string eventName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (eventName == null)
                throw new ArgumentNullException(nameof(eventName));

            if (!eventsCache[source.GetType()].TryGetValue(eventName, out EventInfo eventInfo))
                throw new ArgumentException($@"There is no event '{eventName}' in component '{source}'", nameof(eventName));

            MethodInfo invokeMethod = eventInfo.EventHandlerType.GetMethod(nameof(Action.Invoke));
            ParameterInfo[] parameters = invokeMethod?.GetParameters();
            if (invokeMethod?.ReturnType != typeof(void) || parameters.Length != 2 || parameters[0].ParameterType != typeof(object) || !typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType))
                throw new ArgumentException($"Event '{eventName}' does not have regular event handler delegate type.");

            // already added
            if (sources.TryGetValue(source, out var subscriptions) && subscriptions.ContainsKey(eventInfo))
                return this;

            // creating generic info by reflection because the signature must match and EventArgs can vary
            var info = (SubscriptionInfo)Activator.CreateInstance(typeof(SubscriptionInfo<>).MakeGenericType(parameters[1].ParameterType));
            info.Source = source;
            info.EventName = eventName;
            info.Binding = this;

            // subscribing the event by info.Execute
            info.Delegate = Delegate.CreateDelegate(eventInfo.EventHandlerType, info, nameof(SubscriptionInfo<EventArgs>.Execute));
            Reflector.RunMethod(source, eventInfo.GetAddMethod(), info.Delegate);

            if (subscriptions == null)
                sources[source] = new Dictionary<EventInfo, SubscriptionInfo> { { eventInfo, info } };
            else
                subscriptions[eventInfo] = info;

            UpdateSource(source);
            return this;
        }

        public bool RemoveSource(object source)
        {
            if (!sources.TryGetValue(source, out var subscriptions))
                return false;

            foreach (var subscriptionInfo in subscriptions)
                Reflector.RunMethod(source, subscriptionInfo.Key.GetRemoveMethod(), subscriptionInfo.Value.Delegate);

            return sources.Remove(source);
        }

        public ICommandBinding AddStateUpdater(ICommandStateUpdater updater)
        {
            stateUpdaters.Add(updater);
            return this;
        }

        public bool RemoveStateUpdater(ICommandStateUpdater updater)
        {
            return stateUpdaters.Remove(updater);
        }

        public ICommandBinding AddTarget(object target)
        {
            targets.Add(target ?? throw new ArgumentNullException(nameof(target)));
            return this;
        }

        public bool RemoveTarget(object target)
        {
            return targets.Remove(target);
        }

        #endregion

        #region Private Methods

        private void UpdateSource(object source)
        {
            foreach (string propertyName in ((IDictionary<string, object>)state).Keys)
                UpdateSource(source, propertyName);
        }

        private void UpdateSource(object source, string propertyName)
        {
            if (!state.TryGetValue(propertyName, out object stateValue))
                return;

            foreach (ICommandStateUpdater updater in stateUpdaters)
            {
                if (updater.TryUpdateState(source, propertyName, stateValue))
                    return;
            }

            DefaultUpdateState(source, propertyName, stateValue);
        }

        private void InvokeCommand<TEventArgs>(CommandSource<TEventArgs> source)
            where TEventArgs : EventArgs
        {
            if (!state.Enabled)
                return;

            ICommand<TEventArgs> cmd = command as ICommand<TEventArgs> ?? new CommandGenericWrapper<TEventArgs>(command);
            if (targets.IsNullOrEmpty())
                cmd.Execute(source, state, null);
            else
            {
                foreach (object target in targets)
                {
                    cmd.Execute(source, state, target);
                    if (disposed || !state.Enabled)
                        return;
                }
            }
        }

        #endregion

        #region Event handlers

        private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            foreach (IComponent component in sources.Keys)
                UpdateSource(component, e.PropertyName);
        }

        #endregion

        #endregion

        #endregion
    }
}