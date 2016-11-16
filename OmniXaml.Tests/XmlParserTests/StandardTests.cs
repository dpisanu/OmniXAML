﻿namespace OmniXaml.Tests.XmlParserTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Model;

    [TestClass]
    public class StandardTests : XamlToTreeParserTestsBase
    {
        [TestMethod]
        public void ObjectAndDirectProperties()
        {
            var tree = Parse(@"<Window xmlns=""root"" Title=""Saludos"" />");
        }

        [TestMethod]
        public void InnerStringProperty()
        {
            var tree = Parse(@"<Window xmlns=""root""><Window.Content>Hola</Window.Content></Window>");
        }

        [TestMethod]
        public void InnerComplexProperty()
        {
            var tree = Parse(@"<Window xmlns=""root""><Window.Content><TextBlock /></Window.Content></Window>");
        }
        
        [TestMethod]
        public void AttachedPropertyInsideElement()
        {
            var tree = Parse(@"<Window xmlns=""root""><Grid.Row>1</Grid.Row></Window>");
            Assert.AreEqual(
                new ConstructionNode(typeof(Window))
                {
                    Assignments = new[]
                    {
                        new MemberAssignment
                        {
                            Member = Member.FromAttached<Grid>("Row"),
                            SourceValue = "1"
                        },
                    }
                },
                tree);
        }

        [TestMethod]
        public void ImmutableFromContent()
        {
            var tree = Parse(@"<MyImmutable xmlns=""root"">hola</MyImmutable>");
        }

        [TestMethod]
        public void ContentPropertyDirectContent()
        {
            var tree = Parse(@"<Window xmlns=""root""><TextBlock /></Window>");
        }

        [TestMethod]
        public void ContentPropertyDirectContentText()
        {
            var tree = Parse(@"<TextBlock xmlns=""root"">Hello</TextBlock>");
        }

        [TestMethod]
        public void ContentPropertyDirectContentTextInsideChild()
        {
            var tree = Parse(@"<Window xmlns=""root""><TextBlock>Saludos cordiales</TextBlock></Window>");
        }
        
        [TestMethod]
        public void MarkupExtension()
        {
            var tree = Parse(@"<Window xmlns=""root"" Content=""{Simple}"" />");
        }

        [TestMethod]
        public void CData()
        {
            var tree = Parse(@"<Window xmlns=""root""><Window.Content><![CDATA[Hello]]></Window.Content></Window>");
        }

        [TestMethod]
        public void XmlNs()
        {
            var tree = Parse(@"<Window xmlns=""root"" xmlns:a=""custom"">
                                    <Window.Content>         
                                        <a:CustomControl />                           
                                    </Window.Content>
                                </Window>");
        }

        [TestMethod]
        public void AttachedPropertyFromAnotherNs()
        {
            var tree = Parse(@"<Window xmlns=""root"" xmlns:a=""custom"" a:CustomGrid.Value=""1"" />");
        }

        [TestMethod]
        public void ClrNs()
        {
            var tree = Parse(@"<Window xmlns=""using:OmniXaml.Tests.Model;Assembly=OmniXaml.Tests"" />");
        }        
    }
}