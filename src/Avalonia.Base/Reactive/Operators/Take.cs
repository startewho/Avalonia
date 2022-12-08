using System;

namespace Avalonia.Reactive.Operators;

internal sealed class Take<TSource> : IObservable<TSource>
{
    private readonly IObservable<TSource> _source;
    private readonly int _count;

    public Take(IObservable<TSource> source, int count)
    {
        _source = source;
        _count = count;
    }
    
    public IDisposable Subscribe(IObserver<TSource> observer)
    {
        return _source.Subscribe(new _(_count, observer));
    }

    internal sealed class _ : IdentitySink<TSource>
    {
        private int _remaining;

        public _(int count, IObserver<TSource> observer)
            : base(observer)
        {
            _remaining = count;
        }

        public override void OnNext(TSource value)
        {
            if (_remaining > 0)
            {
                --_remaining;
                ForwardOnNext(value);

                if (_remaining == 0)
                {
                    ForwardOnCompleted();
                }
            }
        }
    }
}

