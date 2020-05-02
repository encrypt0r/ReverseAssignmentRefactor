using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace ReverseAssignment
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ReverseAssignmentCodeRefactoringProvider)), Shared]
    internal class ReverseAssignmentCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            // TODO: Replace the following code with your own analysis, generating a CodeAction for each refactoring to offer

            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var ourNode = root.FindNode(context.Span);

            var list = new List<AssignmentExpressionSyntax>();

            if (ourNode is AssignmentExpressionSyntax assign)
            {
                list.Add(assign);
            }
            else if (ourNode is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement is ExpressionStatementSyntax expression &&
                        expression.Expression is AssignmentExpressionSyntax assignment &&
                        context.Span.Contains(assignment.Span))
                    {
                        list.Add(assignment);
                    }
                }
            }

            //var nodes = root.DescendantNodes(n => 
            //((context.Span.Start < n.FullSpan.Start && context.Span.End > n.FullSpan.Start) ||
            //(context.Span.End < n.FullSpan.End && context.Span.End > n.FullSpan.Start)
            //)

            //&& n is AssignmentExpressionSyntax).ToArray();

            if (list.Count > 0)
            {
                // For any type declaration node, create a code action to reverse the identifier text.
                var action = CodeAction.Create("Reverse assignment(s)", c => ReverseAssignment(context.Document, list, c));
                context.RegisterRefactoring(action);
            }
        }

        private async Task<Document> ReverseAssignment(Document document, IEnumerable<AssignmentExpressionSyntax> nodes, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document);

            foreach (var assignment in nodes)
            {
                if (IsAssignable(assignment.Right))
                {
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    var right = semanticModel.GetTypeInfo(assignment.Right).Type;
                    var left = semanticModel.GetTypeInfo(assignment.Left).Type;

                    var conversion = semanticModel.Compilation.ClassifyConversion(right, left);
                    if (conversion.Exists)
                    {
                        var replacedAssignment = SyntaxFactory.AssignmentExpression(
                               SyntaxKind.SimpleAssignmentExpression,
                               assignment.Right.WithoutTrivia(),
                               assignment.Left.WithoutTrivia());

                        if (assignment.HasLeadingTrivia)
                        {
                            replacedAssignment = replacedAssignment.WithLeadingTrivia(assignment.GetLeadingTrivia());
                        }

                        editor.ReplaceNode(assignment, replacedAssignment);
                    }
                }
            }

            return editor.GetChangedDocument();
        }

        private bool IsAssignable(ExpressionSyntax expression)
        {
            // Only Propeties, variables, and indexers are assignable
            // Properties must be checked for whether they are writable or not

            switch (expression)
            {
                case IdentifierNameSyntax _:
                    return true;

                case ElementAccessExpressionSyntax element:
                    return IsAssignable(element.Expression);

                case MemberAccessExpressionSyntax member:
                    return IsAssignable(member.Expression);
            }

            return false;
        }
    }
}
