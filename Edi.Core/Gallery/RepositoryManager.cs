using Edi.Core.Gallery.Definition;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;

namespace Edi.Core.Gallery
{
    public sealed class RepositoryManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<Type, RepositoryEntry>
            _repositories = new();
        private readonly ConcurrentDictionary<Type, SemaphoreSlim>
            _semaphores = new();
        private string _path = string.Empty;
        private long _pathVersion;

        public RepositoryManager(
            IServiceProvider serviceProvider,
            DefinitionRepository definitions)
        {
            _serviceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
            _repositories[typeof(DefinitionRepository)] =
                new RepositoryEntry(definitions);
        }

        public async Task<T> GetRepositoryAsync<T>()
            where T : class, IRepository
            => (T)await GetRepositoryAsync(typeof(T));

        public T GetRepository<T>()
            where T : class, IRepository
            => GetRepositoryAsync<T>().GetAwaiter().GetResult();

        public IEnumerable<IRepository> CreatedRepositories
            => _repositories.Values
                .Select(entry => entry.Repository)
                .ToArray();

        public async Task ChangePath(string newPath)
        {
            Volatile.Write(ref _path, newPath ?? string.Empty);
            Interlocked.Increment(ref _pathVersion);

            var repositoryTypes = _repositories.Keys.ToArray();
            foreach (var repositoryType in repositoryTypes)
                await GetRepositoryAsync(repositoryType);
        }

        internal bool IsCreated<T>() where T : class, IRepository
            => _repositories.ContainsKey(typeof(T));

        private async Task<IRepository> GetRepositoryAsync(Type type)
        {
            if (!typeof(IRepository).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"{type.FullName} is not a repository type.",
                    nameof(type));
            }

            var semaphore =
                _semaphores.GetOrAdd(type, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (!_repositories.TryGetValue(type, out var entry))
                {
                    entry = new RepositoryEntry(
                        await CreateRepository(type));
                    _repositories[type] = entry;
                }

                while (entry.InitializedPathVersion
                       != Volatile.Read(ref _pathVersion))
                {
                    var version = Volatile.Read(ref _pathVersion);
                    var path = Volatile.Read(ref _path);
                    await InitializeDependencies(type);
                    await entry.Repository.Init(path);
                    entry.InitializedPathVersion = version;
                }

                return entry.Repository;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<IRepository> CreateRepository(Type type)
        {
            var constructor = type.GetConstructors()
                .OrderByDescending(candidate =>
                    candidate.GetParameters().Length)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Repository {type.FullName} has no public constructor.");

            var arguments = new List<object>();
            foreach (var parameter in constructor.GetParameters())
            {
                if (typeof(IRepository).IsAssignableFrom(
                        parameter.ParameterType))
                {
                    arguments.Add(
                        await GetRepositoryAsync(parameter.ParameterType));
                    continue;
                }

                arguments.Add(_serviceProvider.GetRequiredService(
                    parameter.ParameterType));
            }

            return (IRepository)constructor.Invoke(arguments.ToArray());
        }

        private async Task InitializeDependencies(Type type)
        {
            var constructor = type.GetConstructors()
                .OrderByDescending(candidate =>
                    candidate.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null)
                return;

            foreach (var dependency in constructor.GetParameters()
                         .Select(parameter => parameter.ParameterType)
                         .Where(parameterType =>
                             typeof(IRepository).IsAssignableFrom(
                                 parameterType)
                             && parameterType != type))
            {
                await GetRepositoryAsync(dependency);
            }
        }

        private sealed class RepositoryEntry(IRepository repository)
        {
            public IRepository Repository { get; } = repository;
            public long InitializedPathVersion { get; set; } = -1;
        }
    }
}
