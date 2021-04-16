using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components
{
    /// <summary>
    /// Manages the storage for components and services that are part of a Blazor application.
    /// </summary>
    public interface IPersistentComponentStateStore
    {
        /// <summary>
        /// Gets the persisted state from the store.
        /// </summary>
        /// <returns>The persisted state.</returns>
        Task<IDictionary<string, ReadOnlySequence<byte>>> GetPersistedStateAsync();

        /// <summary>
        /// Persists the serialized state into the storage.
        /// </summary>
        /// <param name="state">The serialized state to persist.</param>
        /// <returns>A <see cref="Task" /> that completes when the state is persisted to disk.</returns>
        Task PersistStateAsync(IReadOnlyDictionary<string, ReadOnlySequence<byte>> state);
    }
}
