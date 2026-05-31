// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Datamodel.Data;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;


[MemoryPackable]
public partial class NumberSeriesDto
{
	public NumberPointDto[] Points { get; set; } = [];

	[MemoryPackConstructor, JsonConstructor]
	public NumberSeriesDto() { }

	public NumberSeriesDto(NumberSeries numberSeries)
	{
		Points = new NumberPointDto[numberSeries.PointCount];
		for (int i = 0; i < numberSeries.PointCount; i++)
		{
			Points[i] = new NumberPointDto
			{
				Offset = numberSeries.GetOffset(i),
				Value = numberSeries.GetValue(i)
			};
		}
	}

	public NumberSeries ToNumberSeries()
	{
		NumberSeries numberSeries = new();

		numberSeries.Clear();

		for (int i = 0; i < Points.Length; i++)
		{
			numberSeries.SetOffset(i, Points[i].Offset);
			numberSeries.SetValue(i, Points[i].Value);
		}

		return numberSeries;
	}
}

[MemoryPackable]
public partial class NumberPointDto
{
	public float Offset { get; set; }
	public float Value { get; set; }
}


public class NumberSeriesJsonConverter : JsonConverter<NumberSeries>
{
	public override NumberSeries Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("Expected start of object");
		}

		NumberSeries numberSeries = new();

		numberSeries.Clear();

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return numberSeries;
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
			{
				throw new JsonException("Expected property name");
			}

			string propertyName = reader.GetString()!;

			if (propertyName == "Points")
			{
				reader.Read();
				if (reader.TokenType != JsonTokenType.StartArray)
				{
					throw new JsonException("Expected start of array for points");
				}

				int pointIndex = 0;
				while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				{
					if (reader.TokenType != JsonTokenType.StartObject)
					{
						throw new JsonException("Expected start of point object");
					}

					float offset = 0f;
					float val = 0f;

					while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
					{
						string pointProperty = reader.GetString()!;
						reader.Read();

						switch (pointProperty)
						{
							case "Offset":
								offset = reader.GetSingle();
								break;
							case "Value":
								val = reader.GetSingle();
								break;
						}
					}

					numberSeries.SetOffset(pointIndex, offset);
					numberSeries.SetValue(pointIndex, val);
					pointIndex++;
				}
			}
		}

		throw new JsonException("Unexpected end of JSON");
	}

	public override void Write(Utf8JsonWriter writer, NumberSeries value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WritePropertyName("Points");
		writer.WriteStartArray();

		for (int i = 0; i < value.PointCount; i++)
		{
			writer.WriteStartObject();
			writer.WriteNumber("Offset", value.GetOffset(i));
			writer.WriteNumber("Value", value.GetValue(i));
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}
