﻿using System;
using System.IO;
using System.Linq;
using JetBrains.Application.Components;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.TestFramework;
using NUnit.Framework;
using Roflcopter.Plugin.TodoItems;
using JetBrains.Application.Settings;
using JetBrains.Util;

namespace Roflcopter.Plugin.Tests.TodoItems
{
    [TestFixture]
    [TestNetFramework4]
    public class TodoItemsCountTests : BaseTestWithSingleProject
    {
        protected override string RelativeTestDataPath => Path.Combine(base.RelativeTestDataPath, "..");

        [Test]
        public void TodoItemsCounts()
        {
            Test((consumer, _) =>
            {
                Assert.That(consumer.TodoItemsCounts, Is.Not.Null);
                Assert.That(consumer.TodoItemsCounts.Select(x => (x.Definition.ToString(), x.Count)), Is.EqualTo(new[]
                {
                    ("Bug", 2),
                    ("Todo", 5),
                }));
            });
        }

        [Test]
        public void TodoItemsCountsWithCondition()
        {
            Test((consumer, settings) =>
            {
                var definitionText = "Todo\n Todo  [Important] ";
                RunGuarded(() => settings.SetValue((TodoItemsCountSettings s) => s.Definitions, definitionText));

                Assert.That(consumer.TodoItemsCounts, Is.Not.Null);
                Assert.That(consumer.TodoItemsCounts.Select(x => (x.Definition.ToString(), x.Count)), Is.EqualTo(new[]
                {
                    ("Todo", 5),
                    ("Todo[Important]", 3),
                }));
            });
        }

        [Test]
        public void TodoItemsCountsWithDisabledSetting()
        {
            Test((consumer, settings) =>
            {
                settings.SetValue((TodoItemsCountSettings s) => s.IsEnabled, false);

                Assert.That(consumer.TodoItemsCounts, Is.Null);
            });
        }

        [Test]
        public void TodoItemsCountsWithEmptyDefinitions()
        {
            Test((consumer, settings) =>
            {
                settings.SetValue((TodoItemsCountSettings s) => s.Definitions, "");

                Assert.That(consumer.TodoItemsCounts, Is.Null);
            });
        }

        [Test]
        public void TodoItemsCounts_ConsumerUpdateRequestSignal()
        {
            Test((consumer, _) =>
            {
                var oldUpdateCounter = consumer.UpdateCounter;

                RunGuarded(() => consumer.UpdateRequestSignal.Fire());

                Assert.That(consumer.UpdateCounter, Is.EqualTo(oldUpdateCounter + 1));
            });
        }

        private void Test(Action<TestTodoItemsCountConsumer, IContextBoundSettingsStore> action)
        {
            var files = new[] { "Sample.cs", "Sample.xml" };

            ExecuteWithinSettingsTransaction(settings =>
            {
                WithSingleProject(
                    files.Select(x => GetTestDataFilePath2(x).FullPath),
                    (lifetime, solution, project) =>
                    {
                        solution.GetComponent<TodoItemsCountProvider>().NotNull();
                        var consumer = ShellInstance.GetComponent<TestTodoItemsCountConsumer>();

                        action(consumer, settings);
                    });

                // Disable to solve issues with TodoItemsCountProvider-updates during termination of
                // the "settings transaction":
                settings.SetValue((TodoItemsCountSettings s) => s.IsEnabled, false);
            });
        }
    }
}
