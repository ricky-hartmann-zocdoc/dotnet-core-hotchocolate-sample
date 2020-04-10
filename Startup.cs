using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GreenDonut;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Stitching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GraphQLHCSample
{
    // Complex types just mean they can resolve fields as properties or functions provided on a type in the schema
    [ComplexType]
    public class Provider
    {
        // When using SDL first, the application will fail to start if any field
        // in the schema is not resolvable to either a property or function.
        // You can try commenting this out see the following error:
        // The field `Provider.id` has no resolver. - Type: Provider
        public string Id { get; set; }
    }
    
    [ComplexType]
    public class Query
    {
        public async Task<Provider> Provider(IResolverContext context, [DataLoader] ProviderDataLoader dl, string id)
        {
            // anything added to context can be accessed from context elsewhere
            context.ContextData["Foo"] = "example";
            return await dl.LoadAsync(id);
        }
    }
    
    [ComplexType]
    public class Location
    {
        public Task<string[]> Availability(IResolverContext context)
        {
            // ComplexTypes are general business objects, but you can also add function resolvers to them
            // If you want to DI a service, you can either get it from attribute passed in or ask the context
            // context.Service<ISomeKindOfService>()
            return Task.FromResult(new[]
            {
                // Remember the context item set in the root query?
                $"{Name}: {context.ContextData["Foo"]}"
            });
        }
        
        public string Name { get; set; }
    }
    
    [ComplexType]
    public class Review
    {
        public string Id { get; set; }
        public int BedsideRating { get; set; }
    }
    
    // Resolver types are add additional aggregators/fetchers on an existing complex type that can resolve fields with functions
    // but since its not the base object, it needs to have it passed in in the function call.
    // It can take constructor params for DI like a normal service
    [Resolver(typeof(Provider))]
    public class ProviderResolver
    {
        public string Name(
            // by default, the engine gives you the object you're resolving off of
            Provider d, 
            // as well as the current resolving context to share request data
            IResolverContext context, 
            // you can also ask it to resolve services for you on the function call
            [Service] ProviderFetchService _)
        {
            // maybe I want to blend data already fetched
            return $"Dr. {context.ContextData["Foo"]} {d.Id}";
        }

        public List<Location> Locations() =>
            new[] {new Location {Name = "1"}, new Location {Name = "2"}}.ToList();
        // Resolver and ComplexType methods can either be synchronous or Task based
        public Task<Location> Location(string id) => Task.FromResult(new Location());
        public IEnumerable<Review> Reviews(int limit) => new List<Review>();
    }
    
    /* data loaders are able to aggregate many of the same type into a batched query
        query {
            provider(id: 1) { ... }
            provider(id: 2) { ... }
        }
        
        makes a single call to fetch provider data with ids 1 and 2, but each can be handled independently
    */
    public class ProviderDataLoader : DataLoaderBase<string, Provider>
    {
        private readonly ProviderFetchService _fetchService;
        public ProviderDataLoader(ProviderFetchService fetchService)
        {
            _fetchService = fetchService;
        }
        protected override async Task<IReadOnlyList<Result<Provider>>> FetchAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
        {
            return (await _fetchService.GetProvidersByIds(keys))
                .Select(Result<Provider>.Resolve).ToList();
        }
    }
    
    // this is just a dummy service to mimic taking a list of ids and returning values
    public class ProviderFetchService
    {
        public ValueTask<IEnumerable<Provider>> GetProvidersByIds(IEnumerable<string> ids)
        {
            return new ValueTask<IEnumerable<Provider>>(ids.Select(x => new Provider {Id = x}));
        }
    }
    
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ProviderFetchService>();
            // If you want to stitch another schema to your own, you just add an http client
            // that can get picked up in AddSchemaFromHttp
            // services.AddHttpClient("legacy", (sp, client) =>
            // {
            //     client.BaseAddress = new Uri("https://api.zocdoc.com/directory/v2/gql");
            // });

            services.AddSingleton<IQueryResultSerializer, JsonQueryResultSerializer>();
            services.AddGraphQLSubscriptions();
            services.AddDataLoaderRegistry();
            services.AddStitchedSchema(b => 
                b
                    // .AddSchemaFromHttp("legacy")
                    .AddSchema("new", SchemaBuilder.New()
                        .AddDocumentFromFile("./Schema.graphql")
                        .BindTypesFromAttributes()
                        .Create()
                    )
                    .SetExecutionOptions(new QueryExecutionOptions { TracingPreference = TracingPreference.OnDemand })
            ).AddDataLoader<ProviderDataLoader>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseGraphQL();
            app.UsePlayground();
            app.UseRouting();
        }
    }
}