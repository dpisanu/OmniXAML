﻿namespace OmniXaml.Parsers.ProtoParser
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml;
    using Typing;

    public class XamlProtoInstructionParser : IProtoParser
    {
        private readonly IRuntimeTypeSource typeContext;
        private readonly ProtoInstructionBuilder instructionBuilder;
        private IXmlReader reader;
        private AttributeParser attributeParser;
        private readonly TextFormatter textFormatter = new TextFormatter();

        public XamlProtoInstructionParser(IRuntimeTypeSource typeContext)
        {
            this.typeContext = typeContext;
            instructionBuilder = new ProtoInstructionBuilder(typeContext);
        }

        public IEnumerable<ProtoXamlInstruction> Parse(IXmlReader stream)
        {
            this.reader = stream;
            attributeParser = new AttributeParser(reader);
            reader.Read();

            return ParseElement();
        }

        private IEnumerable<ProtoXamlInstruction> ParseEmptyElement(XamlType xamlType, NamespaceDeclaration namespaceDeclaration, AttributeFeed attributes)
        {
            var emptyElement = instructionBuilder.EmptyElement(xamlType.UnderlyingType, namespaceDeclaration);
            return CommonNodesOfElement(xamlType, emptyElement, attributes);
        }

        private IEnumerable<ProtoXamlInstruction> CommonNodesOfElement(XamlType owner, ProtoXamlInstruction elementToInject, AttributeFeed attributeFeed)
        {
            var attributes = attributeFeed;

            foreach (var instruction in attributes.PrefixRegistrations.Select(ConvertAttributeToNsPrefixDefinition)) yield return instruction;

            yield return elementToInject;

            foreach (var instruction in attributes.Directives.Select(ConvertDirective)) yield return instruction;
            foreach (var instruction in attributes.RawAttributes.Select(a => ConvertAttributeToNode(owner, a))) yield return instruction;
        }

        private IEnumerable<ProtoXamlInstruction> ParseExpandedElement(XamlType xamlType, NamespaceDeclaration namespaceDeclaration, AttributeFeed attributes)
        {
            var element = instructionBuilder.NonEmptyElement(xamlType.UnderlyingType, namespaceDeclaration);
            foreach (var instruction in CommonNodesOfElement(xamlType, element, attributes)) yield return instruction;

            reader.Read();

            foreach (var instruction in ParseInnerTextIfAny()) yield return instruction;
            foreach (var instruction in ParseNestedElements(xamlType)) yield return instruction;

            yield return instructionBuilder.EndTag();
        }

        private IEnumerable<ProtoXamlInstruction> ParseNestedElements(XamlType xamlType)
        {
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                SkipWhitespaces();

                var isPropertyElement = reader.LocalName.Contains(".");
                if (isPropertyElement)
                {
                    foreach (var instruction in ParseNestedProperty(xamlType)) yield return instruction;

                    reader.Read();
                }
                else
                {
                    foreach (var instruction in ParseChildren()) yield return instruction;
                }
            }
        }

        private IEnumerable<ProtoXamlInstruction> ParseNestedProperty(XamlType xamlType)
        {
            var propertyLocator = PropertyLocator.Parse(reader.Name);
            var namespaceDeclaration = new NamespaceDeclaration(reader.Namespace, reader.Prefix);

            yield return InjectPropertyInstruction(xamlType, propertyLocator, namespaceDeclaration);

            reader.Read();

            foreach (var p in ParseInnerTextIfAny()) yield return p;

            SkipWhitespaces();

            if (reader.NodeType != XmlNodeType.EndElement)
            {
                foreach (var instruction in ParseChildren()) { yield return instruction; }
            }

            yield return instructionBuilder.EndTag();
        }

        private ProtoXamlInstruction InjectPropertyInstruction(XamlType xamlType, PropertyLocator propertyLocator, NamespaceDeclaration namespaceDeclaration)
        {
            if (IsNormalProperty(xamlType, propertyLocator))
            {
                return instructionBuilder.NonEmptyPropertyElement(xamlType.UnderlyingType, propertyLocator.PropertyName, namespaceDeclaration);
            }
            else
            {
                var owner = typeContext.GetByPrefix(propertyLocator.Prefix, propertyLocator.OwnerName);
                return instructionBuilder.ExpandedAttachedProperty(owner.UnderlyingType, propertyLocator.PropertyName, namespaceDeclaration);
            }
        }

        private static bool IsNormalProperty(XamlType xamlType, PropertyLocator propertyLocator)
        {
            return !propertyLocator.IsDotted || propertyLocator.IsDotted && propertyLocator.OwnerName == xamlType.Name;
        }

        private IEnumerable<ProtoXamlInstruction> ParseInnerTextIfAny()
        {
            if (reader.NodeType == XmlNodeType.Text)
            {
                yield return instructionBuilder.Text(FormatText(reader.Value));
                reader.Read();
            }
        }

        private string FormatText(string rawText)
        {
            return textFormatter.Format(rawText);
        }

        private IEnumerable<ProtoXamlInstruction> ParseChildren()
        {
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                foreach (var instruction in ParseElement()) { yield return instruction; }

                yield return instructionBuilder.Text();

                reader.Read();
                SkipWhitespaces();
            }
        }

        private void AssertValidElement()
        {
            if (!(reader.NodeType == XmlNodeType.Element && !reader.LocalName.Contains(".")))
            {
                throw new ParseException("The root should be an element.");
            }
        }

        // TODO: Refactor this shit.
        private ProtoXamlInstruction ConvertAttributeToNode(XamlType containingType, AttributeAssignment rawAttributeAssignment)
        {
            MutableMember member;

            if (rawAttributeAssignment.Locator.IsDotted)
            {
                member = GetMemberForDottedLocator(rawAttributeAssignment.Locator);
            }
            else
            {
                if (IsNameDirective(rawAttributeAssignment.Locator, containingType))
                {
                    return instructionBuilder.Directive(CoreTypes.Name, rawAttributeAssignment.Value);
                }

                member = containingType.GetMember(rawAttributeAssignment.Locator.PropertyName);
            }

            var namespaceDeclaration = new NamespaceDeclaration(rawAttributeAssignment.Locator.Namespace, rawAttributeAssignment.Locator.Prefix);
            return instructionBuilder.Attribute(member, rawAttributeAssignment.Value, namespaceDeclaration.Prefix);
        }

        private bool IsNameDirective(XamlName propertyLocator, XamlType ownerType)
        {
            return propertyLocator.PropertyName == ownerType.RuntimeNamePropertyMember?.Name;
        }

        private MutableMember GetMemberForDottedLocator(PropertyLocator propertyLocator)
        {
            var ownerName = propertyLocator.Owner.PropertyName;
            var ownerPrefix = propertyLocator.Owner.Prefix;

            var owner = typeContext.GetByPrefix(ownerPrefix, ownerName);

            MutableMember member = owner.GetAttachableMember(propertyLocator.PropertyName);
            return member;
        }

        private AttributeFeed GetAttributes()
        {
            return attributeParser.Read();
        }

        private IEnumerable<ProtoXamlInstruction> ParseElement()
        {
            SkipWhitespaces();

            AssertValidElement();

            var attributes = GetAttributes();

            var prefixRegistrations = attributes.PrefixRegistrations.ToList();

            RegisterPrefixes(prefixRegistrations);

            var prefix = reader.Prefix;
            var ns = reader.Namespace;
            var namespaceDeclaration = new NamespaceDeclaration(ns, prefix);

            var childType = typeContext.GetByPrefix(namespaceDeclaration.Prefix, reader.LocalName);


            if (reader.IsEmptyElement)
            {
                foreach (var instruction in ParseEmptyElement(childType, namespaceDeclaration, attributes)) yield return instruction;
            }
            else
            {
                foreach (var instruction in ParseExpandedElement(childType, namespaceDeclaration, attributes)) yield return instruction;
            }
        }

        private void RegisterPrefixes(IEnumerable<NsPrefix> prefixRegistrations)
        {
            foreach (var prefixRegistration in prefixRegistrations)
            {
                var registration = new PrefixRegistration(prefixRegistration.Prefix, prefixRegistration.Namespace);
                typeContext.RegisterPrefix(registration);
            }
        }

        private void SkipWhitespaces()
        {
            while (reader.NodeType == XmlNodeType.Whitespace)
            {
                reader.Read();
            }
        }

        private ProtoXamlInstruction ConvertDirective(DirectiveAssignment assignment)
        {
            return instructionBuilder.Directive(assignment.Directive, assignment.Value);
        }

        private ProtoXamlInstruction ConvertAttributeToNsPrefixDefinition(NsPrefix prefix)
        {
            return instructionBuilder.NamespacePrefixDeclaration(prefix.Prefix, prefix.Namespace);
        }

        private class TextFormatter
        {
            private readonly Regex whitespacesRegex = new Regex(@"\s+");

            public string Format(string rawText)
            {
                return whitespacesRegex
                    .Replace(rawText, " ")
                    .Trim();
            }
        }
    }
}