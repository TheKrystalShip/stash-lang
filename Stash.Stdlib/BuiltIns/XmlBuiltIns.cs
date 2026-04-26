namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>xml</c> namespace built-in functions for XML parsing, serialization, and querying.
/// </summary>
public static class XmlBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("xml");

        // XmlNode struct
        ns.Struct("XmlNode", [
            new BuiltInField("tag",      "string"),
            new BuiltInField("attrs",    "dict"),
            new BuiltInField("text",     "string"),
            new BuiltInField("children", "array"),
        ]);

        // XmlParseOptions struct
        ns.Struct("XmlParseOptions", [
            new BuiltInField("preserveWhitespace", "bool"),
        ]);

        // XmlStringifyOptions struct
        ns.Struct("XmlStringifyOptions", [
            new BuiltInField("indent",      "int"),
            new BuiltInField("declaration", "bool"),
            new BuiltInField("encoding",    "string"),
        ]);

        // xml.parse(text, options?) → XmlNode
        ns.Function("parse", [Param("text", "string"), Param("options?", "XmlParseOptions")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("xml.parse: expected 1 or 2 arguments.");

                string text = SvArgs.String(args, 0, "xml.parse");
                bool preserveWhitespace = false;

                if (args.Length > 1 && !args[1].IsNull)
                {
                    if (args[1].AsObj is not StashInstance opts)
                        throw new RuntimeError("xml.parse: options must be an XmlParseOptions struct.", errorType: "TypeError");

                    var pwVal = opts.GetField("preserveWhitespace", null);
                    if (!pwVal.IsNull)
                    {
                        if (!pwVal.IsBool)
                            throw new RuntimeError("xml.parse: preserveWhitespace must be a boolean.", errorType: "TypeError");
                        preserveWhitespace = pwVal.AsBool;
                    }
                }

                try
                {
                    var loadOptions = preserveWhitespace ? LoadOptions.PreserveWhitespace : LoadOptions.None;
                    var doc = XDocument.Parse(text, loadOptions);
                    if (doc.Root is null)
                        throw new RuntimeError("xml.parse: document has no root element.");
                    return StashValue.FromObj(XElementToNode(doc.Root, preserveWhitespace));
                }
                catch (XmlException ex)
                {
                    throw new RuntimeError($"xml.parse: invalid XML — {ex.LineNumber},{ex.LinePosition}: {ex.Message}");
                }
                catch (RuntimeError) { throw; }
                catch (Exception ex)
                {
                    throw new RuntimeError($"xml.parse: failed to parse XML — {ex.Message}");
                }
            },
            returnType: "XmlNode",
            isVariadic: true,
            documentation: "Parses an XML string into an XmlNode tree.\n@param text XML string to parse\n@param options Optional XmlParseOptions struct\n@return Root XmlNode");

        // xml.stringify(node, options?) → string
        ns.Function("stringify", [Param("node", "XmlNode"), Param("options?", "XmlStringifyOptions")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw new RuntimeError("xml.stringify: expected 1 or 2 arguments.");

                if (args[0].AsObj is not StashInstance nodeInst)
                    throw new RuntimeError("xml.stringify: first argument must be an XmlNode.", errorType: "TypeError");

                int indent = 2;
                bool declaration = false;
                string encoding = "UTF-8";

                if (args.Length > 1 && !args[1].IsNull)
                {
                    if (args[1].AsObj is not StashInstance opts)
                        throw new RuntimeError("xml.stringify: options must be an XmlStringifyOptions struct.", errorType: "TypeError");

                    var indentVal = opts.GetField("indent", null);
                    if (!indentVal.IsNull)
                    {
                        if (!indentVal.IsInt)
                            throw new RuntimeError("xml.stringify: indent must be an integer.", errorType: "TypeError");
                        indent = (int)indentVal.AsInt;
                    }

                    var declVal = opts.GetField("declaration", null);
                    if (!declVal.IsNull)
                    {
                        if (!declVal.IsBool)
                            throw new RuntimeError("xml.stringify: declaration must be a boolean.", errorType: "TypeError");
                        declaration = declVal.AsBool;
                    }

                    var encVal = opts.GetField("encoding", null);
                    if (!encVal.IsNull)
                    {
                        if (encVal.AsObj is not string encStr)
                            throw new RuntimeError("xml.stringify: encoding must be a string.", errorType: "TypeError");
                        encoding = encStr;
                    }
                }

                try
                {
                    var element = NodeToXElement(nodeInst);
                    var sb = new StringBuilder();
                    var settings = new XmlWriterSettings
                    {
                        Indent = indent > 0,
                        IndentChars = new string(' ', Math.Max(0, indent)),
                        OmitXmlDeclaration = !declaration,
                        Encoding = Encoding.UTF8,
                    };

                    using (var writer = XmlWriter.Create(sb, settings))
                    {
                        element.WriteTo(writer);
                    }

                    string xml = sb.ToString();

                    // If declaration requested, patch in the user-specified encoding name
                    if (declaration && !encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                        xml = xml.Replace("encoding=\"utf-8\"", $"encoding=\"{encoding}\"", StringComparison.OrdinalIgnoreCase);

                    return StashValue.FromObj(xml);
                }
                catch (RuntimeError) { throw; }
                catch (Exception ex)
                {
                    throw new RuntimeError($"xml.stringify: failed — {ex.Message}", errorType: "IOError");
                }
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Serializes an XmlNode tree to an XML string.\n@param node Root XmlNode\n@param options Optional XmlStringifyOptions struct\n@return XML string");

        // xml.valid(text) → bool
        ns.Function("valid", [Param("text", "string")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                string text = SvArgs.String(args, 0, "xml.valid");
                if (string.IsNullOrEmpty(text)) return StashValue.False;

                try
                {
                    XDocument.Parse(text);
                    return StashValue.True;
                }
                catch
                {
                    return StashValue.False;
                }
            },
            returnType: "bool",
            documentation: "Checks if a string is valid, well-formed XML.\n@param text String to validate\n@return true if valid XML, false otherwise");

        // xml.query(root, xpath) → array
        ns.Function("query", [Param("root", "XmlNode"), Param("xpath", "string")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                if (args.Length != 2)
                    throw new RuntimeError("xml.query: expected 2 arguments.");

                if (args[0].AsObj is not StashInstance nodeInst)
                    throw new RuntimeError("xml.query: first argument must be an XmlNode.", errorType: "TypeError");

                string xpath = SvArgs.String(args, 1, "xml.query");
                var results = new List<StashValue>();

                try
                {
                    var element = NodeToXElement(nodeInst);
                    var evaluated = element.XPathEvaluate(xpath);

                    if (evaluated is IEnumerable<object> objs)
                    {
                        foreach (object obj in objs)
                        {
                            if (obj is XElement el)
                                results.Add(StashValue.FromObj(XElementToNode(el, false)));
                            else if (obj is XAttribute attr)
                                results.Add(StashValue.FromObj(attr.Value));
                            else if (obj is XText txt)
                                results.Add(StashValue.FromObj(txt.Value));
                            else if (obj is XCData cdata)
                                results.Add(StashValue.FromObj(cdata.Value));
                            else
                                results.Add(StashValue.FromObj(obj?.ToString() ?? ""));
                        }
                    }
                    else if (evaluated is string str)
                    {
                        results.Add(StashValue.FromObj(str));
                    }
                    else if (evaluated is bool b)
                    {
                        results.Add(StashValue.FromBool(b));
                    }
                    else if (evaluated is double d)
                    {
                        results.Add(StashValue.FromFloat(d));
                    }

                    return StashValue.FromObj(results);
                }
                catch (XPathException ex)
                {
                    throw new RuntimeError($"xml.query: invalid XPath expression — {ex.Message}", errorType: "ParseError");
                }
                catch (RuntimeError) { throw; }
                catch (Exception ex)
                {
                    throw new RuntimeError($"xml.query: failed — {ex.Message}", errorType: "IOError");
                }
            },
            returnType: "array",
            documentation: "Queries an XmlNode tree using an XPath expression.\n@param root Root XmlNode\n@param xpath XPath expression\n@return Array of matching XmlNode or string values");

        return ns.Build();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StashInstance XElementToNode(XElement el, bool preserveWhitespace)
    {
        // Attributes dict
        var attrs = new StashDictionary();
        foreach (var attr in el.Attributes())
        {
            if (attr.IsNamespaceDeclaration) continue;
            attrs.Set(attr.Name.LocalName, StashValue.FromObj(attr.Value));
        }

        // Children and direct text
        var children = new List<StashValue>();
        var directText = new StringBuilder();

        foreach (var node in el.Nodes())
        {
            switch (node)
            {
                case XElement childEl:
                    children.Add(StashValue.FromObj(XElementToNode(childEl, preserveWhitespace)));
                    break;

                case XCData cdataNode:
                    directText.Append(cdataNode.Value);
                    children.Add(StashValue.FromObj(MakeXmlNode("#cdata", new StashDictionary(), cdataNode.Value, new List<StashValue>())));
                    break;

                case XText textNode:
                    string textContent = textNode.Value;
                    if (preserveWhitespace || !string.IsNullOrWhiteSpace(textContent))
                    {
                        directText.Append(textContent);
                        children.Add(StashValue.FromObj(MakeXmlNode("#text", new StashDictionary(), textContent, new List<StashValue>())));
                    }
                    break;
            }
        }

        return MakeXmlNode(el.Name.LocalName, attrs, directText.ToString(), children);
    }

    private static StashInstance MakeXmlNode(string tag, StashDictionary attrs, string text, List<StashValue> children)
    {
        var fields = new Dictionary<string, StashValue>
        {
            ["tag"]      = StashValue.FromObj(tag),
            ["attrs"]    = StashValue.FromObj(attrs),
            ["text"]     = StashValue.FromObj(text),
            ["children"] = StashValue.FromObj(children),
        };
        return new StashInstance("XmlNode", fields);
    }

    private static XElement NodeToXElement(StashInstance node)
    {
        string tag = node.GetField("tag", null).AsObj as string ?? "element";

        var el = new XElement(tag);

        // Add attributes
        var attrsVal = node.GetField("attrs", null);
        if (!attrsVal.IsNull && attrsVal.AsObj is StashDictionary attrs)
        {
            foreach (object key in attrs.RawKeys())
            {
                string? val = attrs.Get(key).AsObj as string;
                if (val != null) el.SetAttributeValue(key.ToString()!, val);
            }
        }

        // Add children
        var childrenVal = node.GetField("children", null);
        if (!childrenVal.IsNull && childrenVal.AsObj is List<StashValue> children)
        {
            foreach (StashValue child in children)
            {
                if (child.AsObj is not StashInstance childNode) continue;

                string childTag = childNode.GetField("tag", null).AsObj as string ?? "";
                if (childTag == "#text")
                {
                    string? textVal = childNode.GetField("text", null).AsObj as string;
                    if (textVal != null) el.Add(new XText(textVal));
                }
                else if (childTag == "#cdata")
                {
                    string? cdataVal = childNode.GetField("text", null).AsObj as string;
                    if (cdataVal != null) el.Add(new XCData(cdataVal));
                }
                else
                {
                    el.Add(NodeToXElement(childNode));
                }
            }
        }

        return el;
    }

    // ── Config namespace integration ──────────────────────────────────────────

    /// <summary>Parses an XML string to an XmlNode StashInstance. Called by config.parse/config.read.</summary>
    internal static StashInstance ParseXml(string text)
    {
        try
        {
            var doc = XDocument.Parse(text, LoadOptions.None);
            if (doc.Root is null)
                throw new RuntimeError("xml.parse: document has no root element.", errorType: "ParseError");
            return XElementToNode(doc.Root, false);
        }
        catch (XmlException ex)
        {
            throw new RuntimeError($"xml.parse: invalid XML — {ex.LineNumber},{ex.LinePosition}: {ex.Message}", errorType: "ParseError");
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"xml.parse: failed to parse XML — {ex.Message}", errorType: "ParseError");
        }
    }

    /// <summary>Serializes an XmlNode StashInstance to an XML string with 2-space indentation. Called by config.stringify/config.write.</summary>
    internal static string StringifyXml(StashInstance node, string callerName)
    {
        try
        {
            var element = NodeToXElement(node);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                Encoding = Encoding.UTF8,
            };
            using (var writer = XmlWriter.Create(sb, settings))
            {
                element.WriteTo(writer);
            }
            return sb.ToString();
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new RuntimeError($"{callerName}: XML serialization failed — {ex.Message}", errorType: "IOError");
        }
    }
}
