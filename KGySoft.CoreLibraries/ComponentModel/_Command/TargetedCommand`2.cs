﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: TargetedCommand`2.cs
///////////////////////////////////////////////////////////////////////////////
//  Copyright (C) KGy SOFT, 2005-2020 - All Rights Reserved
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
using System.Runtime.CompilerServices;

#endregion

namespace KGySoft.ComponentModel
{
    /// <summary>
    /// Represents a command, which is unaware of its triggering sources and has one or more bound targets.
    /// <br/>See the <strong>Remarks</strong> section of the <see cref="ICommand"/> interface for details and examples about commands.
    /// </summary>
    /// <typeparam name="TTarget">The type of the target.</typeparam>
    /// <seealso cref="ICommand" />
    public sealed class TargetedCommand<TTarget, TParam> : ICommand, IDisposable
    {
        #region Fields

        private Action<ICommandState, TTarget, TParam> callback;

        #endregion

        #region Constructors

        public TargetedCommand(Action<ICommandState, TTarget, TParam> callback)
        {
            if (callback == null)
                Throw.ArgumentNullException(Argument.callback);
            this.callback = callback;
        }

        public TargetedCommand(Action<TTarget, TParam> callback)
        {
            if (callback == null)
                Throw.ArgumentNullException(Argument.callback);
            this.callback = (_, t, param) => callback.Invoke(t, param);
        }

        #endregion

        #region Methods

        #region Public Methods

        /// <summary>
        /// Releases the delegate passed to the constructor. Should be called if the callback is an instance method, which holds references to other objects.
        /// </summary>
        public void Dispose() => callback = null;

        #endregion

        #region Explicitly Implemented Interface Methods

#if !(NET35 || NET40)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        void ICommand.Execute(ICommandSource source, ICommandState state, object target, object parameter)
        {
            Action<ICommandState, TTarget, TParam> copy = callback;
            if (copy == null)
                Throw.ObjectDisposedException();
            copy.Invoke(state, (TTarget)target, (TParam)parameter);
        }

        #endregion

        #endregion
    }
}