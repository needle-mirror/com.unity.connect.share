using System;
using System.Linq;
using UnityEditor;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// A delegate that represents an action dispatcher
    /// </summary>
    /// <param name="action">The dispatched action</param>
    /// <returns></returns>
    public delegate object Dispatcher(object action);

    /// <summary>
    /// A delegate that represents a State reducer
    /// </summary>
    /// <typeparam name="State"></typeparam>
    /// <param name="previousState"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    public delegate State Reducer<State>(State previousState, object action);

    /// <summary>
    /// A delegate that represents a Middleware capable of altering the state
    /// </summary>
    /// <typeparam name="State"></typeparam>
    /// <param name="store"></param>
    /// <returns></returns>
    public delegate Func<Dispatcher, Dispatcher> Middleware<State>(Store<State> store);

    /// <summary>
    /// A delegate that represents a state change
    /// </summary>
    /// <typeparam name="State"></typeparam>
    /// <param name="action"></param>
    public delegate void StateChangedHandler<State>(State action);

    /// <summary>
    /// manages the communication between all the application components
    /// </summary>
    /// <typeparam name="State"></typeparam>
    public class Store<State>
    {
        /// <summary>
        /// Delegate that reacts on state change
        /// </summary>
        public StateChangedHandler<State> stateChanged;
        State _state;
        readonly Dispatcher _dispatcher;
        readonly Reducer<State> _reducer;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="reducer"></param>
        /// <param name="initialState"></param>
        /// <param name="middlewares"></param>
        public Store(
            Reducer<State> reducer, State initialState = default(State),
            params Middleware<State>[] middlewares)
        {
            this._reducer = reducer;
            this._dispatcher = this.ApplyMiddlewares(middlewares);
            this._state = initialState;
        }

        /// <summary>
        /// Dispatches an action
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public object Dispatch(object action)
        {
            return this._dispatcher(action);
        }

        /// <summary>
        /// The state
        /// </summary>
        public State state
        {
            get { return this._state; }
        }

        Dispatcher ApplyMiddlewares(params Middleware<State>[] middlewares)
        {
            return middlewares.Reverse().Aggregate<Middleware<State>, Dispatcher>(this.InnerDispatch,
                (current, middleware) => middleware(this)(current));
        }

        object InnerDispatch(object action)
        {
            this._state = this._reducer(this._state, action);

            if (this.stateChanged != null)
            {
                this.stateChanged(this._state);
            }

            return action;
        }
    }
}
