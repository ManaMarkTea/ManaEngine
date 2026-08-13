using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ManaUI
{
    /// <summary>
    /// Turns a property lambda into compiled get/set delegates plus the property's name.
    /// <para>
    /// Bindings use lambdas rather than string paths so they are compile-checked and survive a rename.
    /// The reflection cost is paid once per property and cached; steady-state binding evaluation is a
    /// delegate call, not a reflection call, which matters when this runs inside a frame loop.
    /// </para>
    /// </summary>
    internal static class PropertyPath
    {
        /// <summary>
        /// Keyed on the source type as well as the property, not the property alone.
        /// <para>
        /// An inherited property has one PropertyInfo no matter which subclass binds it, so keying on that
        /// made the first caller win: binding Visibility on a StackPanel and then on a Button handed the
        /// second one a Func&lt;StackPanel, Visibility&gt; and threw on the cast. Type-specific properties
        /// hid it -- Text, Value and IsChecked are each declared on exactly one control.
        /// </para>
        /// </summary>
        private static readonly ConcurrentDictionary<(PropertyInfo Property, Type Source), object> GetterCache = new();
        private static readonly ConcurrentDictionary<(PropertyInfo Property, Type Source), object?> SetterCache = new();

        /// <summary>
        /// Resolves a direct property access such as <c>x =&gt; x.Name</c>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown for anything that is not a single property on the lambda's own parameter -- notably
        /// nested paths like <c>x =&gt; x.Layer.Name</c>. An observable binding can only subscribe to the
        /// source it was handed, so a nested path would silently miss changes to the intermediate object.
        /// Use a polled binding for those.
        /// </exception>
        public static (Func<TSource, TValue> Getter, Action<TSource, TValue>? Setter, string Name)
            Resolve<TSource, TValue>(Expression<Func<TSource, TValue>> expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            if (expression.Body is not MemberExpression member || member.Member is not PropertyInfo property)
            {
                throw new ArgumentException(
                    $"'{expression}' is not a property access. Bindings need a direct property, e.g. x => x.Name.",
                    nameof(expression));
            }

            if (member.Expression != expression.Parameters[0])
            {
                throw new ArgumentException(
                    $"'{expression}' is a nested path. An observable binding cannot see changes to the "
                    + "intermediate object, so bind to the intermediate directly or use BindPolled.",
                    nameof(expression));
            }

            var key = (property, typeof(TSource));

            var getter = (Func<TSource, TValue>)GetterCache.GetOrAdd(
                key,
                _ => expression.Compile());

            var setter = (Action<TSource, TValue>?)SetterCache.GetOrAdd(
                key,
                k => BuildSetter<TSource, TValue>(k.Property));

            return (getter, setter, property.Name);
        }

        /// <summary>Compiles a setter, or returns null for a read-only property.</summary>
        private static Action<TSource, TValue>? BuildSetter<TSource, TValue>(PropertyInfo property)
        {
            if (!property.CanWrite || property.SetMethod == null)
            {
                return null;
            }

            var source = Expression.Parameter(typeof(TSource), "source");
            var value = Expression.Parameter(typeof(TValue), "value");

            var assign = Expression.Assign(Expression.Property(source, property), value);
            return Expression.Lambda<Action<TSource, TValue>>(assign, source, value).Compile();
        }
    }
}
