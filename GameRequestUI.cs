using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Text;
using System;
using System.Text.RegularExpressions;
using System.Linq;

namespace NS
{
    public class GameRequestUI : MonoBehaviour
    {
        public TMP_InputField inputField;
        public Button submitButton;
        public TextMeshProUGUI outputText;

        private string apiKey = "API HERE";
        private string apiUrl = "https://api.openai.com/v1/chat/completions";

        private bool isRequesting = false; // To manage ongoing requests
        private string generatedCode = "";

        void Start()
        {
            submitButton.onClick.AddListener(OnSubmit);
        }

        void OnSubmit()
        {
            string userInput = inputField.text;
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(userInput))
            {
                if (!isRequesting)
                {
                    StartCoroutine(SendRequestToChatGPT(userInput));
                }
                else
                {
                    outputText.text = "Please wait, a request is already in progress.";
                }
            }
            else
            {
                outputText.text = "API Key or user input is missing.";
            }
        }

        IEnumerator SendRequestToChatGPT(string userInput)
        {
            isRequesting = true; // Mark that a request is in progress

            RequestData requestData = new RequestData
            {
                model = "gpt-3.5-turbo",
                messages = new RequestData.Message[] {
                    new RequestData.Message {
                        role = "user",
                        content = $"Generate code and list variables for: {userInput}. The code must not have a variable with the type : Rigidbody2D, Transform, Vector3."
                    }
                }
            };
            string json = JsonUtility.ToJson(requestData);
            Debug.Log("JSON Request for code and variables: " + json);

            UnityWebRequest www = new UnityWebRequest(apiUrl, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
                outputText.text = "Error: " + www.error;
                Debug.LogError("Response Code: " + www.responseCode);
                Debug.LogError("Response: " + www.downloadHandler.text);
            }
            else
            {
                string response = www.downloadHandler.text;
                ProcessGeneratedCode(response);
            }

            isRequesting = false; // Mark the end of the request
        }

        void ProcessGeneratedCode(string response)
        {
            ResponseData responseData = JsonUtility.FromJson<ResponseData>(response);
            if (responseData != null && responseData.choices != null && responseData.choices.Length > 0)
            {
                generatedCode = responseData.choices[0].message.content;
                outputText.text = "Generated Code:\n" + generatedCode;
                Debug.Log("Generated code without check: " + generatedCode);

                /*var (variables, methods, usingDirectives) = ExtractCodeElements(generatedCode);

                Dictionary<string, List<string>> libraries = new Dictionary<string, List<string>>
                {
                    { "usingDirectives", usingDirectives }
                };*/

                StartCoroutine(ValidateAndCorrectCodeAfterDelay(generatedCode));
            }
            else
            {
                outputText.text = "Error: Invalid response structure.";
            }
        }

        IEnumerator ValidateAndCorrectCodeAfterDelay(string generatedCode)
        {
            yield return new WaitForSeconds(10f); // Wait for 10 seconds before validating

            yield return ValidateAndCorrectCode(generatedCode);
        }

        IEnumerator ValidateAndCorrectCode(string generatedCode)
        {
            // Send a request to ChatGPT to validate the code
            string validationRequest = $"Correct me this script in C# usable on unity. Check if the libraries match the method used in the code. Check that the methods are correct and that no opening or closing braces are missing. Check for missing semicolons. If all's well, I want a corrected code anyway:\n\n{generatedCode}";

            RequestData requestData = new RequestData
            {
                model = "gpt-3.5-turbo",
                messages = new RequestData.Message[]
                {
                    new RequestData.Message
                    {
                        role = "user",
                        content = validationRequest
                    }
                }
            };

            string json = JsonUtility.ToJson(requestData);

            UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + webRequest.error);
                outputText.text = "Error: " + webRequest.error;
            }
            else
            {
                string response = webRequest.downloadHandler.text;
                Debug.Log("Response: " + response);

                // Extract the corrected code from the response
                ResponseData responseData = JsonUtility.FromJson<ResponseData>(response);
                if (responseData != null && responseData.choices != null && responseData.choices.Length > 0)
                {
                    string correctedCode = responseData.choices[0].message.content;
                    Debug.Log("Generated code with check: "  + correctedCode);
                    outputText.text = correctedCode;
                    var (correctedUsingDirectives, correctedVariables, correctedMethods) = ExtractCodeElements(correctedCode);

                    Dictionary<string, List<string>> correctedLibraries = new Dictionary<string, List<string>>
                    {
                        { "usingDirectives", correctedUsingDirectives }
                    };

                    // Send the corrected code to the GameManager
                    SendToGameManager(correctedLibraries, correctedVariables, correctedMethods);
                }
                else
                {
                    Debug.LogError("Error: Invalid response structure.");
                }
            }
        }

        void SendToGameManager(Dictionary<string, List<string>> libraries, List<VariableInfo> variables, Dictionary<string, string> methods)
        {
            GameManager gameManager = FindObjectOfType<GameManager>();
            if (gameManager != null)
            {
                gameManager.UpdateGameCode(libraries, variables, methods);
            }
        }

        (List<string> usingDirectives, List<VariableInfo> variables, Dictionary<string, string> methods) ExtractCodeElements(string code)
        {
            List<VariableInfo> variables = new List<VariableInfo>();
            Dictionary<string, string> methods = new Dictionary<string, string>();
            List<string> usingDirectives = new List<string>();

            // Extract using directives
            string usingPattern = @"using\s+([\w\.]+);";
            MatchCollection usingMatches = Regex.Matches(code, usingPattern);
            foreach (Match match in usingMatches)
            {
                usingDirectives.Add(match.Value);
            }

            // Remove using directives from code to avoid mistaking them for variables
            code = Regex.Replace(code, usingPattern, string.Empty);

            // Extract variables
            string variablePattern = @"(?:public|private|protected|internal)?\s+(\w+)\s+(\w+)\s*(=\s*[^;]+)?;";
            MatchCollection variableMatches = Regex.Matches(code, variablePattern, RegexOptions.Singleline);

            foreach (Match match in variableMatches)
            {
                if (!IsInsideMethod(match.Index, code))
                {
                    string variableTypeString = match.Groups[1].Value;
                    Debug.Log("vts:" + variableTypeString);
                    string variableName = match.Groups[2].Value;
                    string variableValueString = match.Groups[3].Value?.TrimStart('=', ' ');

                    Type variableType = GetTypeFromString(variableTypeString);
                    if (variableType != null)
                    {
                        object value = GetDefaultValue(variableType, variableValueString);

                        variables.Add(new VariableInfo(variableName, variableType, value));
                    }
                }
            }

            // Extract methods
            string methodPattern = @"void\s+(\w+)\s*\(([^)]*)\)\s*\{([^}]*)\}";
            MatchCollection methodMatches = Regex.Matches(code, methodPattern, RegexOptions.Singleline);

            foreach (Match match in methodMatches)
            {
                string methodName = match.Groups[1].Value;
                string methodBody = match.Groups[3].Value;
                methods[methodName] = methodBody;
            }

            return (usingDirectives, variables, methods);
        }

        bool IsInsideMethod(int index, string code)
        {
            string methodPattern = @"void\s+\w+\s*\(([^)]*)\)\s*\{([^}]*)\}";
            MatchCollection methodMatches = Regex.Matches(code, methodPattern, RegexOptions.Singleline);

            foreach (Match match in methodMatches)
            {
                int methodStartIndex = match.Index;
                int methodEndIndex = match.Index + match.Length;
                if (index > methodStartIndex && index < methodEndIndex)
                {
                    return true;
                }
            }
            return false;
        }

        private Type GetTypeFromString(string typeName)
        {
            switch (typeName)
            {
                case "int":
                    return typeof(int);
                case "float":
                    return typeof(float);
                case "double":
                    return typeof(double);
                case "string":
                    return typeof(string);
                case "bool":
                    return typeof(bool);
                case "char":
                    return typeof(char);
                case "long":
                    return typeof(long);
                case "short":
                    return typeof(short);
                case "byte":
                    return typeof(byte);
                case "Rigidbody2D":
                    return typeof(Rigidbody2D);
                default:
                    // Try to get the type by its name
                    return Type.GetType(typeName) ?? Type.GetType("UnityEngine." + typeName + ", UnityEngine");
            }
        }

        private object GetDefaultValue(Type type, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Activator.CreateInstance(type);
            }

            try
            {
                if (type == typeof(int))
                {
                    return int.Parse(value);
                }
                else if (type == typeof(float))
                {
                    return float.Parse(value.TrimEnd('f', 'F'));
                }
                else if (type == typeof(double))
                {
                    return double.Parse(value.TrimEnd('d', 'D'));
                }
                else if (type == typeof(string))
                {
                    return value;
                }
                else if (type == typeof(bool))
                {
                    return bool.Parse(value);
                }
                else if (type == typeof(char))
                {
                    return char.Parse(value);
                }
                else if (type == typeof(long))
                {
                    return long.Parse(value);
                }
                else if (type == typeof(short))
                {
                    return short.Parse(value);
                }
                else if (type == typeof(byte))
                {
                    return byte.Parse(value);
                }
                else if (type == typeof(Rigidbody2D))
                {
                    // Custom handling for UnityEngine types
                    GameObject go = new GameObject();
                    return go.AddComponent<Rigidbody2D>();
                }
                else
                {
                    return Activator.CreateInstance(type);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse value '{value}' for type '{type}': {e.Message}");
                return Activator.CreateInstance(type);
            }
        }
    }

    [Serializable]
    public class RequestData
    {
        public string model;
        public Message[] messages;

        [Serializable]
        public class Message
        {
            public string role;
            public string content;
        }
    }

    [Serializable]
    public class ResponseData
    {
        public Choice[] choices;

        [Serializable]
        public class Choice
        {
            public Message message;

            [Serializable]
            public class Message
            {
                public string content;
            }
        }
    }
}
