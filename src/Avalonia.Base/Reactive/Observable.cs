using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Reactive;

/// <summary>
/// Provides common observable methods as a replacement for the Rx framework.
/// </summary>
internal static class Observable
{
    public static IObservable<TSource> Create<TSource>(Func<IObserver<TSource>, IDisposable> subscribe)
    {
        return new CreateWithDisposableObservable<TSource>(subscribe);
    }

    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> action)
    {
        return source.Subscribe(new AnonymousObserver<T>(action));
    }

    public static IObservable<TResult> Select<TSource, TResult>(this IObservable<TSource> source, Func<TSource, TResult> selector)
    {
        return Create<TResult>(obs =>
        {
            return source.Subscribe(new AnonymousObserver<TSource>(
                input =>
                {
                    TResult value;
                    try
                    {
                        value = selector(input);
                    }
                    catch (Exception ex)
                    {
                        obs.OnError(ex);
                        return;
                    }

                    obs.OnNext(value);
                }, obs.OnError, obs.OnCompleted));
        });
    }
    
    public static IObservable<TSource> Where<TSource>(this IObservable<TSource> source, Func<TSource, bool> predicate)
    {
        return Create<TSource>(obs =>
        {
            return source.Subscribe(new AnonymousObserver<TSource>(
                input =>
                {
                    bool produce;
                    try
                    {
                        produce = predicate(input);
                    }
                    catch (Exception ex)
                    {
                        obs.OnError(ex);
                        return;
                    }
                    if (produce)
                    {
                        obs.OnNext(input);
                    }
                }, obs.OnError, obs.OnCompleted));
        });
    }

    public static IObservable<TResult> SelectMany<TInput, TResult>(this IObservable<TInput> source, Func<TInput, IObservable<TResult>> selector)
    {
        return Create<TResult>(obs =>
        {
            IDisposable? parentDisposable = null;
            var disposables = new CompositeDisposable(2);

            disposables.Add(source
                .Subscribe(new AnonymousObserver<TInput>(parent =>
                {
                    if (parentDisposable is not null)
                    {
                        parentDisposable.Dispose();
                        disposables.Remove(parentDisposable);
                    }
                    
                    var sub = selector(parent)
                        .Subscribe(new AnonymousObserver<TResult>(obs.OnNext, obs.OnError, obs.OnCompleted));

                    parentDisposable = sub;
                    disposables.Add(sub);
                }, obs.OnError, obs.OnCompleted)));

            return disposables;
        });
    }

    public static IObservable<TResult> CombineLatest<TFirst, TSecond, TResult>(
        this IObservable<TFirst> first, IObservable<TSecond> second,
        Func<TFirst, TSecond, TResult> resultSelector)
    {
        return Create<TResult>(obs =>
        {
            bool hasFirstValue = false, hasSecondValue = false;
            TFirst? firstValue = default;
            TSecond? secondValue = default;
            return new CompositeDisposable(2)
            {
                first.Subscribe(new AnonymousObserver<TFirst>(
                    val =>
                    {
                        firstValue = val;
                        hasFirstValue = true;
                        ProduceOnNext();
                    })),
                second.Subscribe(new AnonymousObserver<TSecond>(
                    val =>
                    {
                        secondValue = val;
                        hasSecondValue = true;
                        ProduceOnNext();
                    }))
            };

            void ProduceOnNext()
            {
                if (hasFirstValue && hasSecondValue)
                {
                    TResult result;
                    try
                    {
                        result = resultSelector(firstValue!, secondValue!);
                    }
                    catch (Exception ex)
                    {
                        obs.OnError(ex);
                        return;
                    }
                    obs.OnNext(result);
                }
            }
        });
    }
    
    public static IObservable<TInput[]> CombineLatest<TInput>(
        this IReadOnlyList<IObservable<TInput>> inputs)
    {
        return Create<TInput[]>(obs =>
        {
            var count = inputs.Count;
            var hasValue = new bool[count];
            var arr = new TInput[count];
            var subs = new CompositeDisposable(count);

            for (var index = 0; index < inputs.Count; index++)
            {
                var indexCopy = index;
                var observable = inputs[indexCopy];
                {
                    subs.Add(observable
                        .Subscribe(new AnonymousObserver<TInput>(val =>
                        {
                            try
                            {
                                arr[indexCopy] = val;
                                hasValue[indexCopy] = true;
                                if (hasValue.All(v => v))
                                {
                                    obs.OnNext(arr);
                                }
                            }
                            catch (Exception ex)
                            {
                                obs.OnError(ex);
                            }
                        }, obs.OnError, obs.OnCompleted)));
                }
            }

            return subs;
        });
    }

    public static IObservable<T> Skip<T>(this IObservable<T> source, int skipCount)
    {
        if (skipCount <= 0)
        {
            throw new ArgumentException("Skip count must be bigger than zero", nameof(skipCount));
        }

        return Create<T>(obs =>
        {
            return source.Subscribe(new AnonymousObserver<T>(
                input =>
                {
                    if (--skipCount == 0)
                    {
                        obs.OnNext(input);
                    }
                }, obs.OnError, obs.OnCompleted));
        });
    }
    
    public static IObservable<T> Take<T>(this IObservable<T> source, int takeCount)
    {
        if (takeCount < 0)
        {
            throw new ArgumentException("Skip count must be bigger than zero", nameof(takeCount));
        }

        if (takeCount == 0)
        {
            return Empty<T>();
        }

        return Create<T>(obs =>
        {
            return source.Subscribe(new AnonymousObserver<T>(
                input =>
                {
                    if (takeCount > 0)
                    {
                        --takeCount;
                        obs.OnNext(input);

                        if (takeCount == 0)
                        {
                            obs.OnCompleted();
                        }
                    }
                }, obs.OnError, obs.OnCompleted));
        });
    }

    public static IObservable<EventArgs> FromEventPattern(Action<EventHandler> addHandler, Action<EventHandler> removeHandler)
    {
        return Create<EventArgs>(observer =>
        {
            var handler = new Action<EventArgs>(observer.OnNext);
            var converted = new EventHandler((_, args) => handler(args));
            addHandler(converted);

            return Disposable.Create(() => removeHandler(converted));
        });
    }
    
    public static IObservable<TEventArgs> FromEventPattern<TEventArgs>(Action<EventHandler<TEventArgs>> addHandler, Action<EventHandler<TEventArgs>> removeHandler)
    {
        return Create<TEventArgs>(observer =>
        {
            var handler = new Action<TEventArgs>(observer.OnNext);
            var converted = new EventHandler<TEventArgs>((_, args) => handler(args));
            addHandler(converted);

            return Disposable.Create(() => removeHandler(converted));
        });
    }

    public static IObservable<T> Return<T>(T value)
    {
        return new ReturnImpl<T>(value);
    }
    
    public static IObservable<T> Empty<T>()
    {
        return EmptyImpl<T>.Instance;
    }
        
    /// <summary>
    /// Returns an observable that fires once with the specified value and never completes.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The observable.</returns>
    public static IObservable<T> SingleValue<T>(T value)
    {
        return new SingleValueImpl<T>(value);
    }
 
    private sealed class SingleValueImpl<T> : IObservable<T>
    {
        private readonly T _value;

        public SingleValueImpl(T value)
        {
            _value = value;
        }
        public IDisposable Subscribe(IObserver<T> observer)
        {
            observer.OnNext(_value);
            return Disposable.Empty;
        }
    }
    
    private sealed class ReturnImpl<T> : IObservable<T>
    {
        private readonly T _value;

        public ReturnImpl(T value)
        {
            _value = value;
        }
        public IDisposable Subscribe(IObserver<T> observer)
        {
            observer.OnNext(_value);
            observer.OnCompleted();
            return Disposable.Empty;
        }
    }
    
    internal sealed class EmptyImpl<TResult> : IObservable<TResult>
    {
        internal static readonly IObservable<TResult> Instance = new EmptyImpl<TResult>();

        private EmptyImpl() { }

        public IDisposable Subscribe(IObserver<TResult> observer)
        {
            observer.OnCompleted();
            return Disposable.Empty;
        }
    }
    
    private sealed class CreateWithDisposableObservable<TSource> : IObservable<TSource>
    {
        private readonly Func<IObserver<TSource>, IDisposable> _subscribe;

        public CreateWithDisposableObservable(Func<IObserver<TSource>, IDisposable> subscribe)
        {
            _subscribe = subscribe;
        }

        public IDisposable Subscribe(IObserver<TSource> observer)
        {
            return _subscribe(observer);
        }
    }
}
