using System;
using System.Linq;
using System.Reflection;
using HotChocolate;
using HotChocolate.Configuration.Bindings;
using HotChocolate.Types;

namespace GraphQLHCSample
{
    /*
     * These attributes and extensions follow a similar pattern as our RegisterServiceAttribute
     * but for GraphQL types so I felt it'd be more natural to register them like this
     */

    [AttributeUsage(AttributeTargets.Class)]
    public class ComplexTypeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)]
    public class ResolverAttribute : Attribute
    {
        public Type Of { get; private set; }
        public ResolverAttribute(Type of)
        {
            Of = of;
        }
    }

    public static class SchemaBuilderExtensions
    {
        public static ISchemaBuilder BindTypesFromAttributes(this ISchemaBuilder builder)
        {
            void AddBindings<T>(Func<Type, IBindingBuilder> builderFunc) where T : Attribute
            {
                var types = Assembly.GetCallingAssembly()
                    .GetTypes()
                    .Where(t => t.GetCustomAttribute<T>() != null)
                    .Select(builderFunc);
                foreach (var type in types)
                {
                    builder.AddBinding(type.Create());
                }
            }            
            
            AddBindings<ResolverAttribute>(t =>
            {
                var attribute = t.GetCustomAttribute<ResolverAttribute>();
                return ResolverTypeBindingBuilder.New()
                    .SetFieldBinding(BindingBehavior.Implicit)
                    .SetResolverType(t)
                    .SetType(attribute.Of);
            });
            
            AddBindings<ComplexTypeAttribute>(t => 
                ComplexTypeBindingBuilder.New()
                    .SetFieldBinding(BindingBehavior.Implicit)
                    .SetType(t));

            return builder;
        }
    }
}