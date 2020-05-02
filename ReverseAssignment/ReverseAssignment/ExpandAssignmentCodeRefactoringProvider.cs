using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
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
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ExpandAssignmentCodeRefactoringProvider)), Shared]
    internal class ExpandAssignmentCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
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

            if (list.Count > 0)
            {
                // For any type declaration node, create a code action to reverse the identifier text.
                var action = CodeAction.Create("Expand assignment(s)", c => ExpandAssignment(context.Document, list, c));
                context.RegisterRefactoring(action);
            }
        }

        // https://github.com/nventive/Uno.Roslyn/blob/master/src/Uno.RoslynHelpers/Content/cs/any/Microsoft/CodeAnalysis/SymbolExtensions.cs
        private static IEnumerable<IPropertySymbol> GetProperties(ITypeSymbol symbol) => symbol.GetMembers().OfType<IPropertySymbol>();

        private async Task<Document> ExpandAssignment(Document document, IEnumerable<AssignmentExpressionSyntax> nodes, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document);
            try
            {

                foreach (var assignment in nodes)
                {
                    if (IsExpandable(assignment.Right))
                    {
                        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                        var right = semanticModel.GetTypeInfo(assignment.Right).Type;
                        var left = semanticModel.GetTypeInfo(assignment.Left).Type;

                        var rightProperties = GetProperties(right).Where(p => p.IsWriteOnly == false).ToArray();
                        var leftProperties = GetProperties(left).Where(p => p.IsReadOnly == false).ToArray();

                        var common = new HashSet<string>(rightProperties.Select(p => p.Name).Intersect(leftProperties.Select(p => p.Name)));

                        var rightPropertiesDict = rightProperties.Where(p => common.Contains(p.Name)).ToDictionary(p => p.Name);
                        var leftPropertiesDict = leftProperties.Where(p => common.Contains(p.Name)).ToDictionary(p => p.Name);

                        SyntaxNode last = assignment.Parent;
                        foreach (var key in leftPropertiesDict.Keys)
                        {
                            var leftProperty = leftPropertiesDict[key];
                            var rightProperty = rightPropertiesDict[key];

                            var leftExpression = SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                assignment.Left,
                                SyntaxFactory.IdentifierName(key)
                                );

                            var rightExpression = SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                assignment.Right,
                                SyntaxFactory.IdentifierName(key)
                                );

                            var replacedAssignment = SyntaxFactory.AssignmentExpression(
                                   SyntaxKind.SimpleAssignmentExpression,
                                   leftExpression,
                                   rightExpression);

                            var statement = SyntaxFactory.ExpressionStatement(replacedAssignment);

                            // https://stackoverflow.com/a/32199086/7003797
                            editor.InsertAfter(last, statement);
                            if (last == assignment.Parent)
                            {
                                editor.RemoveNode(last);
                            }

                            last = statement;
                        }
                    }
                }


                return editor.GetChangedDocument();
            }
            catch (Exception ex)
            {

            }
            return document;
        }

        private bool IsExpandable(ExpressionSyntax expression)
        {
            // Only Propeties, variables, and indexers are assignable
            // Properties must be checked for whether they are writable or not

            switch (expression)
            {
                case IdentifierNameSyntax _:
                    return true;

                //case ElementAccessExpressionSyntax element:
                //    return IsExpandable(element.Expression);

                //case MemberAccessExpressionSyntax member:
                //    return IsExpandable(member.Expression);
            }

            return false;
        }
    }
}
