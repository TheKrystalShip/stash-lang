namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>xml</c> namespace built-in functions for XML parsing, serialization, and querying.
/// </summary>
[StashNamespace]
public static partial class XmlBuiltIns
{
    // ── Struct declarations ───────────────────────────────────────────────────

    /// <summary>An XML node with tag, attributes, text content, and child nodes.</summary>
    [StashStruct]
    public sealed record XmlNode(string Tag, StashDictionary Attrs, string Text, List<StashValue> Children);

    /// <summary>Options for XML parsing.</summary>
    [StashStruct]
    public sealed record XmlParseOptions(bool PreserveWhitespace);

    /// <summary>Options for XML serialization.</summary>
    [StashStruct]
    public sealed record XmlStringifyOptions(long Indent, bool Declaration, string Encoding);

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Parses an XML string into an XmlNode tree.</summary>
    /// <param name="text">XML string to parse</param>
    /// <param name="options">Optional XmlParseOptions struct</param>
    /// <exception cref="StashErrorTypes.TypeError">if options is not an XmlParseOptions struct or a field has the wrong type</exception>
    /// <exception cref="StashErrorTypes.ParseError">if the XML is malformed or the document has no root element</exception>
    /// <returns>Root XmlNode</returns>
    [StashFn(ReturnType = "XmlNode")]
    private static StashValue Parse(string text, StashValue options = default)
    {
        bool preserveWhitespace = false;

        if (!options.IsNull)
        {
            if (options.AsObj is not StashInstance opts)
                throw new TypeError("xml.parse: options must be an XmlParseOptions struct.");

            var pwVal = opts.GetField("preserveWhitespace", null);
            if (!pwVal.IsNull)
            {
                if (!pwVal.IsBool)
                    throw new TypeError("xml.parse: preserveWhitespace must be a boolean.");
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
    }

    /// <summary>Serializes an XmlNode tree to an XML string.</summary>
    /// <param name="node">Root XmlNode</param>
    /// <param name="options">Optional XmlStringifyOptions struct</param>
    /// <exception cref="StashErrorTypes.TypeError">if node is not an XmlNode or options fields have the wrong type</exception>
    /// <exception cref="StashErrorTypes.IOError">if serialization fails</exception>
    /// <returns>XML string</returns>
    [StashFn]
    private static string Stringify(StashValue node, StashValue options = default)
    {
        if (node.AsObj is not StashInstance nodeInst)
            throw new TypeError("xml.stringify: first argument must be an XmlNode.");

        int indent = 2;
        bool declaration = false;
        string encoding = "UTF-8";

        if (!options.IsNull)
        {
            if (options.AsObj is not StashInstance opts)
                throw new TypeError("xml.stringify: options must be an XmlStringifyOptions struct.");

            var indentVal = opts.GetField("indent", null);
            if (!indentVal.IsNull)
            {
                if (!indentVal.IsInt)
                    throw new TypeError("xml.stringify: indent must be an integer.");
                indent = (int)indentVal.AsInt;
            }

            var declVal = opts.GetField("declaration", null);
            if (!declVal.IsNull)
            {
                if (!declVal.IsBool)
                    throw new TypeError("xml.stringify: declaration must be a boolean.");
                declaration = declVal.AsBool;
            }

            var encVal = opts.GetField("encoding", null);
            if (!encVal.IsNull)
            {
                if (encVal.AsObj is not string encStr)
                    throw new TypeError("xml.stringify: encoding must be a string.");
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

            return xml;
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new IOError($"xml.stringify: failed — {ex.Message}");
        }
    }

    /// <summary>Checks if a string is valid, well-formed XML.</summary>
    /// <param name="text">String to validate</param>
    /// <returns>true if valid XML, false otherwise</returns>
    [StashFn]
    private static bool Valid(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            XDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Queries an XmlNode tree using an XPath expression.</summary>
    /// <param name="root">Root XmlNode</param>
    /// <param name="xpath">XPath expression</param>
    /// <exception cref="StashErrorTypes.TypeError">if root is not an XmlNode</exception>
    /// <exception cref="StashErrorTypes.ParseError">if the XPath expression is invalid</exception>
    /// <exception cref="StashErrorTypes.IOError">if the query operation fails</exception>
    /// <returns>Array of matching XmlNode or string values</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Query(StashValue root, string xpath)
    {
        if (root.AsObj is not StashInstance nodeInst)
            throw new TypeError("xml.query: first argument must be an XmlNode.");

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

            return results;
        }
        catch (XPathException ex)
        {
            throw new ParseError($"xml.query: invalid XPath expression — {ex.Message}");
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new IOError($"xml.query: failed — {ex.Message}");
        }
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
                throw new ParseError("xml.parse: document has no root element.");
            return XElementToNode(doc.Root, false);
        }
        catch (XmlException ex)
        {
            throw new ParseError($"xml.parse: invalid XML — {ex.LineNumber},{ex.LinePosition}: {ex.Message}");
        }
        catch (RuntimeError) { throw; }
        catch (Exception ex)
        {
            throw new ParseError($"xml.parse: failed to parse XML — {ex.Message}");
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
            throw new IOError($"{callerName}: XML serialization failed — {ex.Message}");
        }
    }
}
