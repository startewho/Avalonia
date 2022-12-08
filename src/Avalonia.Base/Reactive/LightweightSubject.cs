using System;
using System.Threading;

namespace Avalonia.Reactive;

// internal class LightweightSubject<T> : LightweightObservableBase<T>, IAvaloniaSubject<T>
// {
//     public void OnCompleted()
//     {
//         PublishCompleted();
//     }
//
//     public void OnError(Exception error)
//     {
//         PublishError(error);
//     }
//
//     public void OnNext(T value)
//     {
//         PublishNext(value);
//     }
//
//     protected override void Initialize() { }
//
//     protected override void Deinitialize() { }
// }

public sealed class LightweightSubject<T> : IAvaloniaSubject<T>
    {
        #region Fields

        private SubjectDisposable[] _observers;
        private Exception? _exception;
        private static readonly SubjectDisposable[] Terminated = new SubjectDisposable[0];
        private static readonly SubjectDisposable[] Disposed = new SubjectDisposable[0];

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a subject.
        /// </summary>
        public LightweightSubject() => _observers = Array.Empty<SubjectDisposable>();

        #endregion

        #region Properties

        /// <summary>
        /// Indicates whether the subject has observers subscribed to it.
        /// </summary>
        public bool HasObservers => Volatile.Read(ref _observers).Length != 0;

        /// <summary>
        /// Indicates whether the subject has been disposed.
        /// </summary>
        public bool IsDisposed => Volatile.Read(ref _observers) == Disposed;

        #endregion

        #region Methods

        #region IObserver<T> implementation

        private static void ThrowDisposed() => throw new ObjectDisposedException(string.Empty);

        /// <summary>
        /// Notifies all subscribed observers about the end of the sequence.
        /// </summary>
        public void OnCompleted()
        {
            for (; ; )
            {
                var observers = Volatile.Read(ref _observers);

                if (observers == Disposed)
                {
                    _exception = null;
                    ThrowDisposed();
                    break;
                }

                if (observers == Terminated)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _observers, Terminated, observers) == observers)
                {
                    foreach (var observer in observers)
                    {
                        observer.Observer?.OnCompleted();
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Notifies all subscribed observers about the specified exception.
        /// </summary>
        /// <param name="error">The exception to send to all currently subscribed observers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        public void OnError(Exception error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            for (; ; )
            {
                var observers = Volatile.Read(ref _observers);

                if (observers == Disposed)
                {
                    _exception = null;
                    ThrowDisposed();
                    break;
                }

                if (observers == Terminated)
                {
                    break;
                }

                _exception = error;

                if (Interlocked.CompareExchange(ref _observers, Terminated, observers) == observers)
                {
                    foreach (var observer in observers)
                    {
                        observer.Observer?.OnError(error);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Notifies all subscribed observers about the arrival of the specified element in the sequence.
        /// </summary>
        /// <param name="value">The value to send to all currently subscribed observers.</param>
        public void OnNext(T value)
        {
            var observers = Volatile.Read(ref _observers);

            if (observers == Disposed)
            {
                _exception = null;
                ThrowDisposed();
                return;
            }

            foreach (var observer in observers)
            {
                observer.Observer?.OnNext(value);
            }
        }

        #endregion

        #region IObservable<T> implementation

        /// <summary>
        /// Subscribes an observer to the subject.
        /// </summary>
        /// <param name="observer">Observer to subscribe to the subject.</param>
        /// <returns>Disposable object that can be used to unsubscribe the observer from the subject.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            var disposable = default(SubjectDisposable);
            for (; ; )
            {
                var observers = Volatile.Read(ref _observers);

                if (observers == Disposed)
                {
                    _exception = null;
                    ThrowDisposed();

                    break;
                }

                if (observers == Terminated)
                {
                    var ex = _exception;

                    if (ex != null)
                    {
                        observer.OnError(ex);
                    }
                    else
                    {
                        observer.OnCompleted();
                    }

                    break;
                }

                disposable ??= new SubjectDisposable(this, observer);

                var n = observers.Length;
                var b = new SubjectDisposable[n + 1];

                Array.Copy(observers, 0, b, 0, n);

                b[n] = disposable;

                if (Interlocked.CompareExchange(ref _observers, b, observers) == observers)
                {
                    return disposable;
                }
            }

            return Disposable.Empty;
        }

        private void Unsubscribe(SubjectDisposable observer)
        {
            for (; ; )
            {
                var a = Volatile.Read(ref _observers);
                var n = a.Length;

                if (n == 0)
                {
                    break;
                }

                var j = Array.IndexOf(a, observer);

                if (j < 0)
                {
                    break;
                }

                SubjectDisposable[] b;

                if (n == 1)
                {
                    b = Array.Empty<SubjectDisposable>();
                }
                else
                {
                    b = new SubjectDisposable[n - 1];

                    Array.Copy(a, 0, b, 0, j);
                    Array.Copy(a, j + 1, b, j, n - j - 1);
                }

                if (Interlocked.CompareExchange(ref _observers, b, a) == a)
                {
                    break;
                }
            }
        }

        private sealed class SubjectDisposable : IDisposable
        {
            private LightweightSubject<T> _subject;
            private volatile IObserver<T>? _observer;

            public SubjectDisposable(LightweightSubject<T> subject, IObserver<T> observer)
            {
                _subject = subject;
                _observer = observer;
            }

            public IObserver<T>? Observer => _observer;

            public void Dispose()
            {
                var observer = Interlocked.Exchange(ref _observer, null);
                if (observer == null)
                {
                    return;
                }

                _subject.Unsubscribe(this);
                _subject = null!;
            }
        }

        #endregion

        #region IDisposable implementation

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="Subject{T}"/> class and unsubscribes all observers.
        /// </summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _observers, Disposed);
            _exception = null;
        }

        #endregion

        #endregion
    }
