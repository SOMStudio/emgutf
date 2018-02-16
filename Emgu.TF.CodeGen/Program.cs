﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2018 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Tensorflow;
using Emgu.TF;

namespace Emgu.TF.CodeGen
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting code generation");
            using (Buffer bf = TfInvoke.GetAllOpList())
            using (MemoryStream ms = bf.GetMemoryStream())
            {
                var opList = Tensorflow.OpList.Parser.ParseFrom(ms);

                List<String> codeGenResults = new List<string>();
                foreach (var op in opList.Op)
                {
                    codeGenResults.Add(CodeGen(op));
                }

                String code = String.Join(Environment.NewLine, codeGenResults);
                String page =
                @" 
//----------------------------------------------------------------------------
//  Copyright (C) 2004-2018 by EMGU Corporation. All rights reserved.       
//  This code is automatically generated by a program from Tensorflow "
+ Emgu.TF.TfInvoke.Version +
@".  
//  Please do not modify manually.
//----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using Emgu.TF.Util;
using System.Runtime.InteropServices;

namespace Emgu.TF
{
   public partial class Graph : UnmanagedObject
   {
      " + code + @"
   }
}";
                File.WriteAllText("Graph.g.cs", page);
                Console.WriteLine("Code generation completed");
            }
        }

        private static Dictionary<String, String> _typeMap = new Dictionary<string, string>()
        {
            {"bool", "bool" },
            {"int", "long"},
            {"float", "float" },
            {"tensor", "Tensor" },
            {"string", "string" },
            {"type", "DataType"},
            {"shape", "long[]" }
        };

        private static Dictionary<String, bool> _typeIsStruct = new Dictionary<string, bool>()
        {
            {"bool", true },
            {"int", true},
            {"float", true },
            {"tensor", false },
            {"string", false },
            {"type", true},
            {"shape", false }
        };


        /// <summary>
        /// Check if the type is a list type
        /// </summary>
        /// <param name="typeName">the name of the type</param>
        /// <returns>If it is of list type, return the real type. e.g. "list(int)" results in "int", otherwise, return null</returns>
        private static String IsTypeList(String typeName)
        {
            if (typeName.StartsWith("list(") && typeName.EndsWith(")"))
            {
                return typeName.Substring("list(".Length, typeName.Length - "list()".Length);
            }
            return null;
        }
        private static bool IsTypeKnown(String typeName)
        {
            if (_typeMap.ContainsKey(typeName))
                return true;

            String elementType = IsTypeList(typeName);
            if (!String.IsNullOrEmpty(elementType) && _typeMap.ContainsKey(elementType))
                return true;

            return false;
        }

        private static String CleanUpDescription(String description)
        {
            return description.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", " ").Replace("&&", "&amp;&amp;");
        }

        private static String _sixSpaces = "      ";
        private static String _nineSpaces = "         ";

        private static String CodeGen(OpDef op)
        {

            String defaultStr = String.Format("{1}// Skipped function {0}", op.Name, _sixSpaces);
            if (op.Name.StartsWith("_"))
                return defaultStr;

            //if (op.name.Equals("PaddingFIFOQueueV2"))
            {
                StringBuilder document = new StringBuilder(
                    String.Format("{0}{2}///<summary>{0}{2}///{1}{0}{2}///</summary>{0}",
                    Environment.NewLine,
                    CleanUpDescription(op.Summary),
                    _sixSpaces));
                var outputs = op.OutputArg;

                String[] returnDescs = new string[outputs.Count];
                for (int i = 0; i < outputs.Count; i++)
                {
                    returnDescs[i] = String.Format("{4}///{3} {0}(type: {1}){2}",
                        outputs[i].Name,
                        outputs[i].Type,
                        String.IsNullOrEmpty(CleanUpDescription(outputs[i].Description))
                            ? "."
                            : ": " + CleanUpDescription(outputs[i].Description),
                        outputs.Count > 0 ? String.Format("[{0}]", i) : String.Empty,
                        _sixSpaces);
                }
                String returnDoc = String.Join(Environment.NewLine, returnDescs);
                returnDoc = String.Format("{2}///<return>{0}{1}{0}{2}///</return>{0}", Environment.NewLine, returnDoc, _sixSpaces);
                if (outputs.Count == 0)
                    returnDoc = String.Empty;


                String returnStr = "Operation";

                var inputs = op.InputArg;

                List<String> inputsStrList = new List<string>();
                foreach (var input in inputs)
                {
                    string inputDescription = CleanUpDescription(input.Description);
                    document.AppendFormat("{4}///<param name=\"{0}\">Input to the operation{3} {1}</param>{2}", FixParamName(input.Name), inputDescription, Environment.NewLine, String.IsNullOrEmpty(inputDescription) ? "." : ":", _sixSpaces);
                    inputsStrList.Add(String.Format(" {0} {1} ", "Output", FixParamName(input.Name)));
                }

                String inputsStr = String.Join(",", inputsStrList);

                var attr = op.Attr;
                List<OpDef.Types.AttrDef> unknownAttr = new List<OpDef.Types.AttrDef>();
                List<OpDef.Types.AttrDef> knownAttr = new List<OpDef.Types.AttrDef>();
                foreach (var a in attr)
                {
                    if (IsTypeKnown(a.Type))
                        knownAttr.Add(a);
                    else
                    {
                        unknownAttr.Add(a);
                    }
                }

                var requiredAttr = knownAttr.FindAll(a => a.DefaultValue == null);
                var optionalAttr = knownAttr.FindAll(a => a.DefaultValue != null);

                foreach (var iarg in op.InputArg)
                {
                    if (iarg.TypeAttr != "")
                    {
                        requiredAttr.RemoveAll(a => a.Name.Equals(iarg.TypeAttr));
                        optionalAttr.RemoveAll(a => a.Name.Equals(iarg.TypeAttr));
                    }

                    if (iarg.TypeListAttr != "")
                    {
                        requiredAttr.RemoveAll(a => a.Name.Equals(iarg.TypeListAttr));
                        optionalAttr.RemoveAll(a => a.Name.Equals(iarg.TypeListAttr));
                    }

                    if (iarg.NumberAttr != "")
                    {
                        requiredAttr.RemoveAll(a => a.Name.Equals(iarg.NumberAttr));
                        optionalAttr.RemoveAll(a => a.Name.Equals(iarg.NumberAttr));
                    }
                }
                foreach (var required in requiredAttr)
                {
                    document.AppendFormat("{3}///<param name=\"{0}\">{1}</param>{2}", FixParamName(required.Name), CleanUpDescription(required.Description), Environment.NewLine, _sixSpaces);
                }
                foreach (var optional in optionalAttr)
                {
                    document.AppendFormat("{3}///<param name=\"{0}\">{1}</param>{2}", FixParamName(optional.Name), CleanUpDescription(optional.Description), Environment.NewLine, _sixSpaces);
                }

                document.AppendFormat("{0}///<param name=\"opName\">The name of the operation</param>{1}", _sixSpaces,
                    Environment.NewLine);
                document.Append(returnDoc);

                if (unknownAttr.Count > 0)
                {
                    document.AppendFormat("{2}//The following attributes are not known: {1}{0}",
                        Environment.NewLine,
                        String.Join("; ", unknownAttr.ConvertAll(a => String.Format("{0}: {1}", a.Name, a.Type))),
                        _sixSpaces);
                }

                String requiredStr = String.Join(",", requiredAttr.ConvertAll<String>(
                    a =>
                    {
                        String isTypeList = IsTypeList(a.Type);
                        if (String.IsNullOrEmpty(isTypeList))
                            return String.Format(" {0} {1}", _typeMap[a.Type], a.Name);
                        else
                            return String.Format(" {0}[] {1}", _typeMap[isTypeList], a.Name);
                    }));
                String optionalStr = String.Join(",", optionalAttr.ConvertAll<String>(
                    a =>
                    {
                        String isTypeList = IsTypeList(a.Type);
                        if (String.IsNullOrEmpty(isTypeList))
                        {
                            if (_typeIsStruct[a.Type])
                            {
                                if (a.Type.Equals("int"))
                                    return String.Format(" {0} {1} = {2} ", _typeMap[a.Type], a.Name, a.DefaultValue.I);
                                else if (a.Type.Equals("bool"))
                                    return String.Format(" {0} {1} = {2} ", _typeMap[a.Type], a.Name, a.DefaultValue.B ? "true" : "false");
                                else if (a.Type.Equals("float"))
                                {
                                    String valueString = a.DefaultValue.F.ToString();
                                    if (Single.IsPositiveInfinity(a.DefaultValue.F))
                                        valueString = "Single.PositiveInfinity";
                                    else if (Single.IsNegativeInfinity(a.DefaultValue.F))
                                        valueString = "Single.NegativeInfinity";
                                    else
                                        valueString = valueString + "f";

                                    String res = String.Format(" {0} {1} = {2} ", _typeMap[a.Type], a.Name, valueString);
                                    return res;
                                }
                                else
                                    return String.Format(" {0}? {1} = null ", _typeMap[a.Type], a.Name);
                            }
                            else
                            {
                                return String.Format(" {0} {1} = null ", _typeMap[a.Type], a.Name);
                            }
                        }
                        else
                            return String.Format(" {0}[] {1} = null ", _typeMap[isTypeList], a.Name);
                    }));
                
                List<string> paramList = new List<string>();
                if (!String.IsNullOrEmpty(inputsStr)) paramList.Add(inputsStr);
                if (!String.IsNullOrEmpty(requiredStr)) paramList.Add(requiredStr);
                if (!String.IsNullOrEmpty(optionalStr)) paramList.Add(optionalStr);
                paramList.Add(String.Format("String opName= \"{0}\"", op.Name));
                String paramStr = String.Join(",", paramList);

                StringBuilder body = new StringBuilder(String.Format(
                    "{2}OperationDescription desc = NewOperation(\"{0}\", opName);{1}",
                    op.Name,
                    Environment.NewLine,
                    _nineSpaces));
                List<String> addInputStringList = new List<string>();
                foreach (var i in inputs)
                {
                    addInputStringList.Add(String.Format("{1}desc.AddInput({0});", FixParamName(i.Name), _nineSpaces));
                }
                body.Append(String.Join(Environment.NewLine, addInputStringList));
                body.Append(Environment.NewLine);
                body.Append(String.Join(Environment.NewLine, requiredAttr.ConvertAll(RequiredAttrToString)));
                body.Append(Environment.NewLine);
                body.Append(String.Join(Environment.NewLine, optionalAttr.ConvertAll(OptionalAttrToString)));
                body.Append(Environment.NewLine);
                body.Append(String.Format("{0}return desc.FinishOperation();", _nineSpaces));
                String code = String.Format("{5}public {0} {1} ( {2} ) {3}{5}{{{3}{4}{3}{5}}} ", returnStr, op.Name, paramStr, Environment.NewLine, body.ToString(), _sixSpaces);
                return String.Format("{0}{1}", document, code);
            }

        }

        private static String FixParamName(String name)
        {
            if (name.Equals("params"))
                return "parameters";
            if (name.Equals("ref"))
                return "reference";
            if (name.Equals("event"))
                return "tfEvent";
            return name;
        }

        private static String OptionalAttrToString(OpDef.Types.AttrDef a)
        {
            String funcName = "SetAttr";
            if (a.Type == "shape")
            {
                funcName = "SetAttrShape";
            }
            else if (a.Type == "list(shape)")
            {
                funcName = "SetAttrShapeList";
            }
            if (!String.IsNullOrEmpty(IsTypeList(a.Type)))
            {
                //if this is a list
                return String.Format(
                    "{2}if ({0} != null) desc.{1}(\"{0}\", {0});",
                    a.Name,
                    funcName,
                    _nineSpaces);
            }
            if (_typeIsStruct[a.Type])
            {
                if (a.Type.Equals("int"))
                    return String.Format("{3}if ({0} != {1}) desc.{2}(\"{0}\", {0});", a.Name, a.DefaultValue.I, funcName, _nineSpaces);
                else if (a.Type.Equals("bool"))
                    return String.Format("{3}if ({0} != {1}) desc.{2}(\"{0}\", {0});", a.Name, a.DefaultValue.B ? "true" : "false", funcName, _nineSpaces);
                else if (a.Type.Equals("float"))
                {
                    String valueString = a.DefaultValue.F.ToString();
                    if (Single.IsPositiveInfinity(a.DefaultValue.F))
                        valueString = "Single.PositiveInfinity";
                    else if (Single.IsNegativeInfinity(a.DefaultValue.F))
                        valueString = "Single.NegativeInfinity";
                    else
                        valueString = valueString + "f";

                    return String.Format(
                        "{3}if ({0} != {1}) desc.{2}(\"{0}\", {0});", a.Name, valueString,
                        funcName,
                        _nineSpaces);
                }
                else
                    return String.Format(
                        "{2}if ({0}.HasValue) desc.{1}(\"{0}\", {0}.Value);",
                        a.Name,
                        funcName,
                        _nineSpaces);

            }
            else
            {
                return String.Format("{3}if ({0}{1}) desc.{2}(\"{0}\", {0});",
                    a.Name,
                    " != null",
                    funcName,
                    _nineSpaces);
            }
        }

        private static String RequiredAttrToString(OpDef.Types.AttrDef a)
        {
            String funcName = "SetAttr";
            if (a.Type == "shape")
            {
                funcName = "SetAttrShape";
            }
            else if (a.Type == "list(shape)")
            {
                funcName = "SetAttrShapeList";
            }
            return String.Format("{2}desc.{1}(\"{0}\", {0});",
                a.Name,
                funcName,
                _nineSpaces);
        }

    }
}
