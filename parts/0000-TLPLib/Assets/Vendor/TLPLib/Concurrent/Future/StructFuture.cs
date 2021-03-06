﻿using System;
using com.tinylabproductions.TLPLib.Functional;

namespace com.tinylabproductions.TLPLib.Concurrent {
  struct UnfulfilledFuture {}
  public enum FutureType { Successful, Unfulfilled, ASync }

  /**
   * Struct based future which does not generate garbage if it's actually
   * synchronous.
   **/
  public struct Future<A> {
    /* Future with a known value|unfulfilled future|async future. */
    readonly OneOf<A, UnfulfilledFuture, IHeapFuture<A>> implementation;
    public bool isCompleted => implementation.fold(_ => true, _ => false, f => f.isCompleted);
    public Option<A> value => implementation.fold(F.some, _ => F.none<A>(), f => f.value);

    public FutureType type => implementation.fold(
      _ => FutureType.Successful, 
      _ => FutureType.Unfulfilled, 
      _ => FutureType.ASync
    );

    Future(OneOf<A, UnfulfilledFuture, IHeapFuture<A>> implementation) {
      this.implementation = implementation;
    }

    /* Lift an ordinary value into a future. */
    public static Future<A> successful(A value) {
      return new Future<A>(new OneOf<A, UnfulfilledFuture, IHeapFuture<A>>(value));
    }

    /* Future that will never be completed. */
    public static readonly Future<A> unfulfilled = 
      new Future<A>(new OneOf<A, UnfulfilledFuture, IHeapFuture<A>>(new UnfulfilledFuture()));

    /* Asynchronous heap based future which can be completed later. */
    public static Future<A> async(Act<Promise<A>> body)
      { return async((p, _) => body(p)); }

    /* Asynchronous heap based future which can be completed later. */
    public static Future<A> async(Act<Promise<A>, Future<A>> body) {
      Promise<A> promise;
      var future = @async(out promise);
      body(promise, future);
      return future;
    }

    public static Future<A> async(out Promise<A> promise) {
      var impl = new FutureImpl<A>();
      promise = impl;
      return new Future<A>(new OneOf<A, UnfulfilledFuture, IHeapFuture<A>>(impl));
    }

    public void onComplete(Act<A> action) {
      if (implementation.isA) action(implementation.__unsafeGetA);
      else if (implementation.isC) implementation.__unsafeGetC.onComplete(action);
    }
    
    public Future<B> map<B>(Fn<A, B> mapper) {
      return implementation.fold(
        v => Future<B>.successful(mapper(v)), 
        _ => Future<B>.unfulfilled,
        f => Future<B>.async(p => f.onComplete(v => p.complete(mapper(v))))
      );
    }

    public Future<B> flatMap<B>(Fn<A, Future<B>> mapper) {
      return implementation.fold(
        mapper,
        _ => Future<B>.unfulfilled,
        f => Future<B>.async(p => f.onComplete(v => mapper(v).onComplete(p.complete)))
      );
    }

    /** Filter future on value - if predicate matches turns completed future into unfulfilled. **/
    public Future<A> filter(Fn<A, bool> predicate) {
      var self = this;
      return implementation.fold(
        a => predicate(a) ? self : unfulfilled,
        _ => self,
        f => async(p => f.onComplete(a => { if (predicate(a)) p.complete(a); }))
      );
    }

    /** 
     * Filter & map future on value. If collector returns Some, completes the future, 
     * otherwise - never completes.
     **/
    public Future<B> collect<B>(Fn<A, Option<B>> collector) {
      return implementation.fold(
        a => collector(a).fold(Future<B>.unfulfilled, Future<B>.successful),
        _ => Future<B>.unfulfilled,
        f => Future<B>.async(p => f.onComplete(a => {
          foreach (var b in collector(a)) p.complete(b);
        }))
      );
    }

    /* Waits until both futures yield a result. */
    public Future<Tpl<A, B>> zip<B>(Future<B> fb) => zip(fb, F.t);

    public Future<C> zip<B, C>(Future<B> fb, Fn<A, B, C> mapper) {
      if (implementation.isB || fb.implementation.isB) return Future<C>.unfulfilled;
      if (implementation.isA && fb.implementation.isA)
        return Future.successful(mapper(implementation.__unsafeGetA, fb.implementation.__unsafeGetA));

      var fa = this;
      return Future<C>.async(p => {
        Act tryComplete = () => fa.value.zip(fb.value, mapper).each(ab => p.tryComplete(ab));
        fa.onComplete(a => tryComplete());
        fb.onComplete(b => tryComplete());
      });
    }
  }
}
