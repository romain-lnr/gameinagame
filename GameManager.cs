﻿using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.Emit;

namespace NS
{
    public class GameManager : MonoBehaviour
    {
        private Dictionary<string, Action<GameObject>> compiledMethods = new Dictionary<string, Action<GameObject>>();
        private List<VariableInfo> variables = new List<VariableInfo>();

        public Sprite spriteToAssign; // Référence au sprite à attribuer au GameObject
        private GameObject targetGameObject;

        void Start()
        {
            if (compiledMethods.ContainsKey("Start"))
            {
                compiledMethods["Start"]?.Invoke(targetGameObject);
            }
        }

        void Update()
        {
            if (compiledMethods.ContainsKey("Update"))
            {
                compiledMethods["Update"]?.Invoke(targetGameObject);
            }
        }

        private void FixedUpdate()
        {
            if (compiledMethods.ContainsKey("FixedUpdate"))
            {
                compiledMethods["FixedUpdate"]?.Invoke(targetGameObject);
            }
        }

        void CreateTargetGameObject()
        {
            // Créez un nouveau GameObject
            targetGameObject = new GameObject("TargetGameObject");

            // Vous pouvez ajouter d'autres composants au GameObject si nécessaire
            SpriteRenderer spriteRenderer = targetGameObject.AddComponent<SpriteRenderer>();
            if (spriteToAssign != null)
            {
                spriteRenderer.sprite = spriteToAssign;
            }
            else
            {
                Debug.LogWarning("SpriteToAssign not set.");
            }

            // Positionnez le GameObject dans le monde comme vous le souhaitez
            targetGameObject.transform.position = Vector3.zero;
        }

        public void UpdateGameCode(Dictionary<string, List<string>> libraries, List<VariableInfo> variables, Dictionary<string, string> methods)
        {
            CreateTargetGameObject();
            InitializeVariables(variables);

            List<string> usingDirectives = libraries.ContainsKey("usingDirectives") ? libraries["usingDirectives"] : new List<string>();

            foreach (var method in methods)
            {
                Debug.Log($"Updating method: {method.Key} with body: {method.Value}");
                compiledMethods[method.Key] = CompileMethod(usingDirectives, method.Key, method.Value);
            }
        }


        private void InitializeVariables(List<VariableInfo> variables)
        {
            this.variables.Clear();

            foreach (var variable in variables)
            {
                Debug.Log("Adding variable: " + variable.VariableName + " with type " + variable.VariableType.Name + " with the value " + variable.Value);
                this.variables.Add(variable);
            }
        }

        private Action<GameObject> CompileMethod(List<string> usingDirectives, string methodName, string methodBody)
        {
            string variableDeclarations = "";
            foreach (var variable in variables)
            {
                string variableName = variable.VariableName;
                Type variableType = variable.VariableType;
                object variableValue = variable.Value;

                // Gérer la conversion de la valeur de la variable en chaîne de caractères appropriée
                string valueString = variableValue != null ? variableValue.ToString() : "null";
                if (variableType == typeof(string))
                {
                    valueString = $"\"{valueString}\"";
                }
                else if (variableType == typeof(float))
                {
                    valueString = valueString + "f";
                }

                variableDeclarations += $"public {variableType.Name} {variableName} = {valueString};\n";
            }
            Debug.Log("vd:" + variableDeclarations);
            usingDirectives.Add("using System;");
            string usingStatements = string.Join("\n", usingDirectives);

            string codeToCompile = $@"
            {usingStatements}
            public class DynamicCode {{
                {variableDeclarations}
                public void {methodName}(GameObject currentGameObject) {{
                    var transform = currentGameObject.transform;
                    {methodBody}
                }}
            }}";

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(codeToCompile);
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName: Path.GetRandomFileName(),
                syntaxTrees: new[] { syntaxTree },
                references: new[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(UnityEngine.Debug).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(UnityEngine.Input).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(UnityEngine.MonoBehaviour).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(UnityEngine.Rigidbody2D).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)
                },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        Debug.LogError(diagnostic.ToString());
                    }
                    return null;
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    Assembly assembly = Assembly.Load(ms.ToArray());
                    Type program = assembly.GetType("DynamicCode");
                    MethodInfo method = program.GetMethod(methodName);

                    object instance = Activator.CreateInstance(program);

                    foreach (var variable in variables)
                    {
                        FieldInfo field = program.GetField(variable.VariableName);
                        if (field != null)
                        {
                            field.SetValue(instance, variable.Value);
                        }
                    }

                    return (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), instance, method);
                }
            }
        }
    }
}