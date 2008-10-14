//
// Gendarme.Rules.Smells.AvoidSpeculativeGeneralityRule class
//
// Authors:
//	Néstor Salceda <nestor.salceda@gmail.com>
//
// 	(C) 2007 Néstor Salceda
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Gendarme.Framework;
using Gendarme.Framework.Rocks;
using Gendarme.Rules.Performance;

namespace Gendarme.Rules.Smells {
	
	/// <summary>
	/// This rule allows developers to avoid the Speculative Generality smell. 
	/// Be carefull if you are developing a new framework or a new library, 
	/// because this rule only inspect the assembly, then if you provide an 
	/// abstract base class for extend by thrid party people, then the rule
	/// can warn you. You can ignore the message in this special case.
	/// 
	/// We can detect this kind of smell looking for some points:
	/// <list type="bullet">
    	/// <item><description>Abstract classes without responsability</description></item>
    	/// <item><description>Unnecesary delegation.</description></item>
	/// <item><description>Unused parameters.</description></item>
    	/// </list>
	/// </summary>
	/// <example>
	/// Bad example:
	/// An abstract class with only one subclass.
	/// <code>
	/// public abstract class AbstractClass {
	/// 	public abstract void MakeStuff ();
	/// }
	/// 
	/// public class OverriderClass : AbstractClass {
	/// 	public override void MakeStuff ()
	///	{
	///	}
	/// }
	/// </code>
	/// If you use Telephone class only in one client, perhaps you don't need this kind of delegation. 
	/// <code>
	/// public class Person {
	///	int age;
	/// 	string name;
	///	Telephone phone;
	/// }
 	///
	/// public class Telephone {
	/// 	int areaCode;
	/// 	int phone;
	/// }
	/// </code>
	/// </example>
	/// <example>
	/// Good example:
	/// <code>
	/// public abstract class OtherAbstractClass{
	///	public abstract void MakeStuff ();
	/// }
	/// 
	/// public class OtherOverriderClass : OtherAbstractClass {
	/// 	public override void MakeStuff ()
	/// 	{
	/// 	}
	/// }
	/// 
	/// public class YetAnotherOverriderClass : OtherAbstractClass {
	/// 	public override void MakeStuff ()
	/// 	{
	/// 	}
	/// }
	/// </code>
	/// </example>

	[Problem ("If you will need the feature in the future then you should implement it in the future.")]
	[Solution ("You can apply various refactorings: Collapse Hierarchy, Inline Class, Remove Parameter or Rename Method.")]
	public class AvoidSpeculativeGeneralityRule : Rule, ITypeRule {

		public int CountInheritedClassesFrom (TypeDefinition baseType)
		{
			int count = 0;
			foreach (AssemblyDefinition assembly in Runner.Assemblies) {
				foreach (ModuleDefinition module in assembly.Modules) {
					foreach (TypeDefinition type in module.Types) {
						if ((baseType == type.BaseType) || (type.BaseType != null &&
							(baseType.FullName == type.BaseType.FullName))) {
							count++;
						}
					}
				}
			}
			return count;
		}

		private void CheckAbstractClassWithoutResponsability (TypeDefinition type)
		{
			if (type.IsAbstract) {
				if (CountInheritedClassesFrom (type) == 1)
					Runner.Report (type, Severity.Medium, Confidence.Normal, "This abstract class has only one class inheritting from.  Abstract classes without responsability are a sign for the Speculative Generality smell.");
			}
		}

		//return true if the method only contains only a single call.
		private static bool OnlyDelegatesCall (MethodDefinition method)
		{
			if (!method.HasBody)
				return false;
			bool onlyOneCallInstruction = false;

			foreach (Instruction instruction in method.Body.Instructions) {
				if (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Calli || instruction.OpCode.Code == Code.Callvirt)
					if (onlyOneCallInstruction)
						return false;
					else
						onlyOneCallInstruction = true;
			}

			return onlyOneCallInstruction;
		}

		private static bool InheritsOnlyFromObject (TypeDefinition type)
		{
			return type.BaseType.FullName == "System.Object" && type.Interfaces.Count == 0;
		}

		private static bool MostlyMethodsDelegatesCall (TypeDefinition type)
		{
			int delegationCounter = 0;
			foreach (MethodDefinition method in type.Methods) {
				if (OnlyDelegatesCall (method))
					delegationCounter++;
			}

			return type.Methods.Count / 2 + 1 <= delegationCounter;
		}

		private void CheckUnnecesaryDelegation (TypeDefinition type)
		{
			if (MostlyMethodsDelegatesCall (type) && InheritsOnlyFromObject (type))
				Runner.Report (type, Severity.Medium, Confidence.Normal, "This class contains a lot of methods that only delegates the call to other.  This king of Delegation could be a sign for Speculative Generality");
		}

		static bool AvoidUnusedParametersRuleScheduled (IRunner runner)
		{
			foreach (IRule rule in runner.Rules) {
				// skip rules that are loaded but inactive
				if (!rule.Active)
					continue;
				if (rule is AvoidUnusedParametersRule)
					return true;
			}
			return false;
		}

		private AvoidUnusedParametersRule avoidUnusedParameters;

		public override void Initialize (IRunner runner)
		{
			base.Initialize (runner);

			// look for AvoidUnusedParametersRule
			// note: we can be re-initialized multiple time (e.g. wizard runner)
			bool scheduled = AvoidUnusedParametersRuleScheduled (runner);
			if (!scheduled) {
				if (avoidUnusedParameters == null)
					avoidUnusedParameters = new AvoidUnusedParametersRule ();
				avoidUnusedParameters.Initialize (Runner);
			} else {
				avoidUnusedParameters = null;
			}
		}

		public RuleResult CheckType (TypeDefinition type)
		{
			if (type.IsGeneratedCode ())
				return RuleResult.DoesNotApply;

			CheckAbstractClassWithoutResponsability (type);
			if (avoidUnusedParameters != null) {
				foreach (MethodDefinition method in type.AllMethods ()) {
					avoidUnusedParameters.CheckMethod (method);
				}
			}

			CheckUnnecesaryDelegation (type);
			return Runner.CurrentRuleResult;
		}
	}
}
