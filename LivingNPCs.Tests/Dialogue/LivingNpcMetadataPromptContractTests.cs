using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LivingNPCs.Dialogue.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

public sealed class LivingNpcMetadataPromptContractTests
{
    [Fact]
    public void CompactInstructionsStayWithinFixedInputBudget()
    {
        string inline = LivingNpcMetadataExtractionPass.BuildInlineInstructions();
        string classifier = LivingNpcMetadataExtractionPass.BuildPromptForTesting(
            new Character("Haley"), new DialogueContext(), "Hello.", "Good morning.", Array.Empty<string>());

        // The original inline contract was 8,656 characters. Keep the prose reduction while
        // allowing minor wording changes; the separate schema check prevents hiding fields.
        Assert.InRange(inline.Length, 1, 6200);
        Assert.InRange(classifier.Length, 1, 6200);
    }

    [Fact]
    public void CompactFieldReferenceCoversEveryRuntimeMetadataFieldAndType()
    {
        string prompt = LivingNpcMetadataExtractionPass.BuildInlineInstructions();
        JObject schema = JObject.Parse(prompt.Split('\n').Single(line => line.StartsWith('{')));

        Assert.Equal(JTokenType.Boolean, schema["complete"]!.Type);
        Assert.True(schema.Value<bool>("complete"));
        schema.Remove("complete");

        foreach ((string field, string typeName) in new[]
        {
            ("travelDecision", "TravelDecisionSchema"),
            ("giftDecision", "GiftDecisionSchema")
        })
        {
            Type type = typeof(LivingNpcMetadataExtractionPass)
                .GetNestedType(typeName, BindingFlags.NonPublic)!;
            Assert.NotNull(type);
            AssertDocumentedShape(type, schema[field]!);
            schema.Remove(field);
        }

        // Derive the expected fields from the runtime DTOs, including ordered help steps;
        // shorter instructions must never silently drop a supported effect or nested key.
        AssertDocumentedShape(typeof(ConversationAnalysis), schema);
    }

    private static void AssertDocumentedShape(Type type, JToken token)
    {
        Assert.NotNull(token);
        if (type == typeof(string) || type == typeof(int) || type == typeof(bool))
        {
            JTokenType expected = type == typeof(string) ? JTokenType.String
                : type == typeof(int) ? JTokenType.Integer
                : JTokenType.Boolean;
            Assert.Equal(expected, token.Type);
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            JArray array = Assert.IsType<JArray>(token);
            Type itemType = type.GetGenericArguments()[0];
            if (itemType == typeof(string))
            {
                Assert.All(array, item => AssertDocumentedShape(itemType, item));
            }
            else
            {
                AssertDocumentedShape(itemType, Assert.Single(array));
            }

            return;
        }

        JObject obj = Assert.IsType<JObject>(token);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<JsonPropertyAttribute>()))
            .Where(entry => entry.Attribute?.PropertyName != null)
            .ToDictionary(entry => entry.Attribute!.PropertyName!, entry => entry.Property.PropertyType, StringComparer.Ordinal);

        Assert.NotEmpty(properties);
        Assert.Equal(properties.Keys.OrderBy(name => name, StringComparer.Ordinal),
            obj.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        foreach ((string name, Type propertyType) in properties)
        {
            AssertDocumentedShape(propertyType, obj[name]!);
        }
    }
}
