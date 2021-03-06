﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using JetBrains.Application.Settings;
using JetBrains.Application.Settings.Extentions;
using JetBrains.DataFlow;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Settings.Cache;
using JetBrains.ReSharper.Feature.Services.TodoItems;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Util;
using JetBrains.Util.DataStructures;

namespace Roflcopter.Plugin.TodoItems
{
    [SolutionComponent]
    public class TodoItemsCountProvider : ICachedSettingsReader<IReadOnlyCollection<TodoItemsCountDefinition>>
    {
        private static readonly ILogger Logger = JetBrains.Util.Logging.Logger.GetLogger(typeof(TodoItemsCountProvider));

        private readonly IPrimaryTodoManager _primaryTodoManager;
        private readonly ISettingsStore _settingsStore;
        private readonly IReadOnlyList<ITodoItemsCountConsumer> _todoItemsCountConsumers;

        private readonly ISettingsCache _settingsCache;

        [NotNull]
        private volatile Lifetime _currentSettingsCacheLifeTime;

        public TodoItemsCountProvider(
            Lifetime lifetime,
            IPrimaryTodoManager primaryTodoManager,
            SolutionSettingsCache solutionSettingsCache,
            ISettingsStore settingsStore,
            IEnumerable<ITodoItemsCountConsumer> todoItemsCountConsumers)
        {
            _primaryTodoManager = primaryTodoManager;
            _settingsCache = solutionSettingsCache;
            _settingsStore = settingsStore;
            _todoItemsCountConsumers = todoItemsCountConsumers.ToIReadOnlyList();

            _primaryTodoManager.FilesWereUpdated.Advise(lifetime, files =>
            {
                // Check for invalid changed files, else we'll get "not valid" exceptions in the 'AllItems' access
                // later (at least as observed during unit test shut down):
                if (files.WhereNotNull().All(x => x.IsValid()))
                    UpdateTodoItemsCounts();
            });

            var settingsCacheGetDataSequentialLifeTime = new SequentialLifetimes(lifetime);
            _currentSettingsCacheLifeTime = settingsCacheGetDataSequentialLifeTime.Next();

            _settingsStore.AdviseChange(lifetime, KeyExposed, () =>
            {
                // We use the following lifetime to solve the issue that this 'ISettingsStore.AdviseChange()' callback
                // arrives earlier than the one used in the cache. => The cache has still the old value when accessed
                // in 'UpdateTodoItemsCounts()'. => Terminate the cache lifetime explicitly.
                _currentSettingsCacheLifeTime = settingsCacheGetDataSequentialLifeTime.Next();

                UpdateTodoItemsCounts();
            });

            foreach (var consumer in _todoItemsCountConsumers)
                consumer.UpdateRequestSignal.Advise(lifetime, () => { UpdateTodoItemsCounts(); });

            // IDEA: Combine the three event sources and execute update in background thread?
        }

        private void UpdateTodoItemsCounts()
        {
            Logger.Verbose(nameof(UpdateTodoItemsCounts) + " ...");

            var definitions = GetTodoItemsCountDefinitions();

            IReadOnlyList<TodoItemsCount> todoItemsCounts = null;

            // IDEA: Maybe also early exit when there are no consumers/they have no visible presentation?

            if (definitions != null && definitions.Count > 0)
            {
                var localTodoItemsCounts = new LocalList<TodoItemsCount>(definitions.Select(x => new TodoItemsCount(x)));

                var allTodoItems = FetchAllTodoItems();

                foreach (var todoItemChunk in allTodoItems.SelectMany(x => x))
                foreach (var todoItem in todoItemChunk.Value) // IDEA: Parallelize?
                {
                    foreach (var newTodoItemsCount in localTodoItemsCounts)
                        newTodoItemsCount.IncreaseIfMatches(todoItem);
                }

                todoItemsCounts = localTodoItemsCounts.ToArray();
            }

            foreach (var consumer in _todoItemsCountConsumers)
                consumer.Update(todoItemsCounts);
        }

        private List<ChunkHashMap<IPsiSourceFile, List<TodoItemBase>>> FetchAllTodoItems()
        {
            using (_primaryTodoManager.Lock())
            using (ReadLockCookie.Create())
            {
                return _primaryTodoManager.AllItems.ToList();
            }
        }

        [CanBeNull]
        private IReadOnlyCollection<TodoItemsCountDefinition> GetTodoItemsCountDefinitions()
        {
            return _settingsCache.GetData(_currentSettingsCacheLifeTime, this);
        }

        public SettingsKey KeyExposed => _settingsStore.Schema.GetKey<TodoItemsCountSettings>();

        [CanBeNull]
        IReadOnlyCollection<TodoItemsCountDefinition> ICachedSettingsReader<IReadOnlyCollection<TodoItemsCountDefinition>>.
            ReadData(IContextBoundSettingsStore store)
        {
            var isEnabled = store.GetValue((TodoItemsCountSettings s) => s.IsEnabled);

            if (!isEnabled)
                return null;

            var definitionsText = store.GetValue((TodoItemsCountSettings s) => s.Definitions);

            var matches = Regex.Matches(definitionsText, @"^\s*(?<Title>.+?)\s*(\[(?<Condition>.*)\]\s*)?$", RegexOptions.Multiline);

            var result = from Match match in matches
                         let title = match.Groups["Title"].Value
                         let condition = match.Groups["Condition"]
                         select new TodoItemsCountDefinition(title, condition.Success ? condition.Value : null);

            return result.ToList();
        }
    }
}
