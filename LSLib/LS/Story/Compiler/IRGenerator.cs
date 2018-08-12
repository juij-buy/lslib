﻿using LSLib.LS.Story.GoalParser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LSLib.LS.Story.Compiler
{
    /// <summary>
    /// Generates IR from story AST.
    /// </summary>
    public class IRGenerator
    {
        private CompilationContext Context;

        public IRGenerator(CompilationContext context)
        {
            Context = context;
        }

        private IRGoal ASTGoalToIR(ASTGoal astGoal)
        {
            var goal = new IRGoal
            {
                InitSection = new List<IRFact>(),
                KBSection = new List<IRRule>(),
                ExitSection = new List<IRFact>(),
                ParentTargetEdges = new List<IRGoalRef>()
            };

            foreach (var fact in astGoal.InitSection)
            {
                goal.InitSection.Add(ASTFactToIR(fact));
            }

            foreach (var rule in astGoal.KBSection)
            {
                goal.KBSection.Add(ASTRuleToIR(goal, rule));
            }

            foreach (var fact in astGoal.ExitSection)
            {
                goal.ExitSection.Add(ASTFactToIR(fact));
            }

            foreach (var refGoal in astGoal.ParentTargetEdges)
            {
                goal.ParentTargetEdges.Add(new IRGoalRef(refGoal));
            }

            return goal;
        }

        private IRRule ASTRuleToIR(IRGoal goal, ASTRule astRule)
        {
            var rule = new IRRule
            {
                Goal = goal,
                Type = astRule.Type,
                Conditions = new List<IRCondition>(),
                Actions = new List<IRStatement>(),
                Variables = new List<IRRuleVariable>(),
                VariablesByName = new Dictionary<String, IRRuleVariable>()
            };

            foreach (var condition in astRule.Conditions)
            {
                rule.Conditions.Add(ASTConditionToIR(rule, condition));
            }

            foreach (var action in astRule.Actions)
            {
                rule.Actions.Add(ASTActionToIR(rule, action));
            }

            return rule;
        }

        private IRStatement ASTActionToIR(IRRule rule, ASTAction astAction)
        {
            if (astAction is ASTGoalCompletedAction)
            {
                var astGoal = astAction as ASTGoalCompletedAction;
                return new IRStatement
                {
                    Func = null,
                    Goal = rule.Goal,
                    Not = false,
                    Params = new List<IRValue>()
                };
            }
            else if (astAction is ASTStatement)
            {
                var astStmt = astAction as ASTStatement;
                var stmt = new IRStatement
                {
                    Func = new IRSymbolRef(new FunctionNameAndArity(astStmt.Name, astStmt.Params.Count)),
                    Goal = null,
                    Not = astStmt.Not,
                    Params = new List<IRValue>()
                };

                foreach (var param in astStmt.Params)
                {
                    stmt.Params.Add(ASTValueToIR(rule, param));
                }

                return stmt;
            }
            else
            {
                throw new InvalidOperationException("Cannot convert unknown AST condition type to IR");
            }
        }

        private IRCondition ASTConditionToIR(IRRule rule, ASTCondition astCondition)
        {
            if (astCondition is ASTFuncCondition)
            {
                var astFunc = astCondition as ASTFuncCondition;
                var func = new IRFuncCondition
                {
                    Func = new IRSymbolRef(new FunctionNameAndArity(astFunc.Name, astFunc.Params.Count)),
                    Not = astFunc.Not,
                    Params = new List<IRValue>()
                };

                foreach (var param in astFunc.Params)
                {
                    func.Params.Add(ASTValueToIR(rule, param));
                }

                return func;
            }
            else if (astCondition is ASTBinaryCondition)
            {
                var astBin = astCondition as ASTBinaryCondition;
                return new IRBinaryCondition
                {
                    LValue = ASTValueToIR(rule, astBin.LValue),
                    Op = astBin.Op,
                    RValue = ASTValueToIR(rule, astBin.RValue)
                };
            }
            else
            {
                throw new InvalidOperationException("Cannot convert unknown AST condition type to IR");
            }
        }

        private IRValue ASTValueToIR(IRRule rule, ASTRValue astValue)
        {
            if (astValue is ASTConstantValue)
            {
                return ASTConstantToIR(astValue as ASTConstantValue);
            }
            else if (astValue is ASTLocalVar)
            {
                var astVar = astValue as ASTLocalVar;
                // TODO - compiler error if type resolution fails
                var type = astVar.Type != null ? Context.LookupType(astVar.Type) : null;
                var ruleVar = rule.FindOrAddVariable(astVar.Name, type);

                return new IRVariable
                {
                    Index = ruleVar.Index,
                    Type = type
                };
            }
            else
            {
                throw new InvalidOperationException("Cannot convert unknown AST value type to IR");
            }
        }

        private IRFact ASTFactToIR(ASTFact astFact)
        {
            var fact = new IRFact
            {
                Database = new IRSymbolRef(new FunctionNameAndArity(astFact.Database, astFact.Elements.Count)),
                Not = astFact.Not,
                Elements = new List<IRConstant>()
            };

            foreach (var element in astFact.Elements)
            {
                fact.Elements.Add(ASTConstantToIR(element));
            }

            return fact;
        }

        // TODO - un-copy + move to constant code?
        private ValueType ConstantTypeToValueType(IRConstantType type)
        {
            switch (type)
            {
                case IRConstantType.Unknown: return null;
                // TODO - lookup type ID from enum
                case IRConstantType.Integer: return Context.TypesById[1];
                case IRConstantType.Float: return Context.TypesById[3];
                case IRConstantType.String: return Context.TypesById[4];
                case IRConstantType.Name: return Context.TypesById[5];
                default: throw new ArgumentException("Invalid IR constant type");
            }
        }

        private IRConstant ASTConstantToIR(ASTConstantValue element)
        {
            ValueType type;
            if (element.TypeName != null)
            {
                type = Context.LookupType(element.TypeName);
                if (type == null)
                {
                    Context.Log.Error(null, DiagnosticCode.UnresolvedType,
                        String.Format("Type \"{0}\" does not exist", element.TypeName));
                }
            }
            else
            {
                type = ConstantTypeToValueType(element.Type);
            }

            return new IRConstant
            {
                ValueType = element.Type,
                Type = type,
                InferredType = element.TypeName != null,
                IntegerValue = element.IntegerValue,
                FloatValue = element.FloatValue,
                StringValue = element.StringValue
            };
        }

        public ASTGoal ParseGoal(string path)
        {
            using (var file = new FileStream(path, FileMode.Open))
            {
                var scanner = new GoalScanner();
                scanner.SetSource(file);
                var parser = new GoalParser.GoalParser(scanner);
                bool parsed = parser.Parse();

                if (parsed)
                {
                    return parser.GetGoal();
                }
                else
                {
                    return null;
                }
            }
        }

        public IRGoal GenerateGoalIR(ASTGoal goal)
        {
            return ASTGoalToIR(goal);
        }
    }
}
