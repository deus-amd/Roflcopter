using System;
using JetBrains.Annotations;
using JetBrains.Application.Progress;
using JetBrains.DocumentManagers.Transactions;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Psi;
using JetBrains.TextControl;
using JetBrains.Util;

namespace Roflcopter.Plugin.MismatchedFileNames
{
    [QuickFix]
    public class MismatchedFileNameHighlightingQuickFix : QuickFixBase
    {
        private readonly MismatchedFileNameHighlighting _highlighting;

        public MismatchedFileNameHighlightingQuickFix(MismatchedFileNameHighlighting highlighting)
        {
            _highlighting = highlighting;
        }

        public override string Text => $"Rename file to '{_highlighting.ExpectedFileName}'";

        public override bool IsAvailable(IUserDataHolder cache)
        {
            // TODO
            return true;
        }

        [CanBeNull]
        protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
        {
            var newName = _highlighting.ExpectedFileName;

            var projectFile = _highlighting.TreeNode.GetSourceFile().ToProjectFile();

            return _ =>
            {
                if (projectFile.Location.Directory.Combine(newName).ExistsFile)
                {
                    MessageBox.ShowError($"File '{newName}' is already exists", $"Can't rename '{projectFile.Location.Name}'");
                }
                else
                {
                    using (var transactionCookie = solution.CreateTransactionCookie(DefaultAction.Commit, Text))
                        transactionCookie.Rename(projectFile, newName);
                }
            };
        }
    }
}
