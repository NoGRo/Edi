using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core.Gallery
{
    public class RepositoryManager
    {
        private readonly IServiceProvider _serviceProvider;
        private string _configPath;
        private readonly ConcurrentDictionary<Type, IRepository> _createdRepositories = new();
        private readonly ConcurrentDictionary<Type, SemaphoreSlim> _semaphores = new();

        public RepositoryManager(IServiceProvider serviceProvider, string configPath = "")
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _configPath = configPath;
        }

        public async Task<T> GetRepositoryAsync<T>() where T : class, IRepository
        {
            var type = typeof(T);

            // Early escape si ya está inicializado
            if (_createdRepositories.TryGetValue(type, out var existing) && existing.IsInitialized)
                return (T)existing;

            var semaphore = _semaphores.GetOrAdd(type, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (_createdRepositories.TryGetValue(type, out existing) && existing.IsInitialized)
                    return (T)existing;

                var constructor = type.GetConstructors().First();
                var parameters = constructor.GetParameters();
                var args = await Task.WhenAll(parameters.Select(async p =>
                    typeof(IRepository).IsAssignableFrom(p.ParameterType) && p.ParameterType != type
                        ? await GetRepositoryAsync(p.ParameterType)
                        : p.ParameterType == typeof(string) ? _configPath
                        : _serviceProvider.GetService(p.ParameterType)));

                var repository = (T)constructor.Invoke(args);
                await repository.Init(_configPath);
                _createdRepositories.TryAdd(type, repository);
                return repository;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task ChangePath(string newPath)
        {
            _configPath = newPath;
            var updateTasks = _createdRepositories.Values
                .Where(r => r.IsInitialized)
                .Select(r => r.Init(newPath));
            await Task.WhenAll(updateTasks);
        }

        private async Task<object> GetRepositoryAsync(Type type)
        {
            var method = typeof(RepositoryManager).GetMethod(nameof(GetRepositoryAsync), BindingFlags.Instance | BindingFlags.Public)!
                .MakeGenericMethod(type);
            return await (Task<object>)method.Invoke(this, null)!;
        }
    }
}