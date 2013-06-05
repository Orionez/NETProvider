﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if (!EF_6)
using System.Data.Metadata.Edm;
#else
using System.Data.Entity.Core.Metadata.Edm;
#endif

namespace FirebirdSql.Data.Entity
{
	static class SsdlToFb
	{
		public static string Transform(StoreItemCollection storeItems, string providerManifestToken)
		{
			var result = new StringBuilder();

			if (storeItems == null)
			{
				result.Append("-- No input.");
				return result.ToString();
			}

			result.Append("-- Tables");
			result.AppendLine();
			result.Append(string.Join(Environment.NewLine, Tables(storeItems)));
			result.AppendLine();
			result.Append("-- Foreign Key Constraints");
			result.AppendLine();
			result.Append(string.Join(Environment.NewLine, ForeignKeyConstraints(storeItems)));
			result.AppendLine();
			result.AppendLine();
			result.Append("-- EOF");

			return result.ToString();
		}

		static IEnumerable<string> Tables(StoreItemCollection storeItems)
		{
			foreach (var entitySet in storeItems.GetItems<EntityContainer>()[0].BaseEntitySets.OfType<EntitySet>())
			{
				var result = new StringBuilder();
				var additionalColumnComments = new Dictionary<string, string>();
				result.AppendFormat("RECREATE TABLE {0} (", SqlGenerator.QuoteIdentifier(MetadataHelpers.GetTableName(entitySet)));
				result.AppendLine();
				foreach (var property in MetadataHelpers.GetProperties(entitySet.ElementType))
				{
					var column = GenerateColumn(property);
					result.Append("\t");
					result.Append(column.Item1);
					result.Append(",");
					result.AppendLine();
					foreach (var item in column.Item2)
						additionalColumnComments.Add(item.Key, item.Value);
				}
				result.AppendFormat("CONSTRAINT {0} PRIMARY KEY ({1})",
					SqlGenerator.QuoteIdentifier(string.Format("PK_{0}", MetadataHelpers.GetTableName(entitySet))),
					string.Join(", ", entitySet.ElementType.KeyMembers.Select(pk => SqlGenerator.QuoteIdentifier(pk.Name))));
				result.AppendLine();
				result.Append(");");
				result.AppendLine();
				foreach (var identity in entitySet.ElementType.KeyMembers.Where(pk => pk.TypeUsage.Facets.Contains("StoreGeneratedPattern") && (StoreGeneratedPattern)pk.TypeUsage.Facets["StoreGeneratedPattern"].Value == StoreGeneratedPattern.Identity).Select(i => i.Name))
				{
					additionalColumnComments.Add(identity, "#PK_GEN#");
				}
				foreach (var comment in additionalColumnComments)
				{
					result.AppendFormat("COMMENT ON COLUMN {0}.{1} IS '{2}';",
						SqlGenerator.QuoteIdentifier(MetadataHelpers.GetTableName(entitySet)),
						SqlGenerator.QuoteIdentifier(comment.Key),
						comment.Value);
					result.AppendLine();
				}
				yield return result.ToString();
			}
		}

		static IEnumerable<string> ForeignKeyConstraints(StoreItemCollection storeItems)
		{
			foreach (var associationSet in storeItems.GetItems<EntityContainer>()[0].BaseEntitySets.OfType<AssociationSet>())
			{
				var result = new StringBuilder();
				ReferentialConstraint constraint = associationSet.ElementType.ReferentialConstraints.Single<ReferentialConstraint>();
				AssociationSetEnd end = associationSet.AssociationSetEnds[constraint.FromRole.Name];
				AssociationSetEnd end2 = associationSet.AssociationSetEnds[constraint.ToRole.Name];
				result.AppendFormat("ALTER TABLE {0} ADD CONSTRAINT {1} FOREIGN KEY ({2})",
					SqlGenerator.QuoteIdentifier(MetadataHelpers.GetTableName(end2.EntitySet)),
					SqlGenerator.QuoteIdentifier(string.Format("FK_{0}", associationSet.Name)),
					string.Join(", ", constraint.ToProperties.Select(fk => SqlGenerator.QuoteIdentifier(fk.Name))));
				result.AppendLine();
				result.AppendFormat("REFERENCES {0}({1})",
					SqlGenerator.QuoteIdentifier(MetadataHelpers.GetTableName(end.EntitySet)),
					string.Join(", ", constraint.FromProperties.Select(pk => SqlGenerator.QuoteIdentifier(pk.Name))));
				result.AppendLine();
				result.AppendFormat("ON DELETE {0}",
					end.CorrespondingAssociationEndMember.DeleteBehavior == OperationAction.Cascade ? "CASCADE" : "NO ACTION");
				result.Append(";");
				yield return result.ToString();
			}
		}

		static Tuple<string, IDictionary<string, string>> GenerateColumn(EdmProperty property)
		{
			var column = new StringBuilder();
			var columnComments = new Dictionary<string, string>();
			column.Append(SqlGenerator.QuoteIdentifier(property.Name));
			column.Append(" ");
			column.Append(SqlGenerator.GetSqlPrimitiveType(property.TypeUsage));
			switch (MetadataHelpers.GetEdmType<PrimitiveType>(property.TypeUsage).PrimitiveTypeKind)
			{
				case PrimitiveTypeKind.Boolean:
					column.AppendFormat(" CHECK ({0} IN (1,0))", SqlGenerator.QuoteIdentifier(property.Name));
					columnComments.Add(property.Name, "#BOOL#");
					break;
				case PrimitiveTypeKind.Guid:
					columnComments.Add(property.Name, "#GUID#");
					break;
			}
			if (!property.Nullable)
			{
				column.Append(" NOT NULL");
			}
			return Tuple.Create<string, IDictionary<string, string>>(column.ToString(), columnComments);
		}
	}
}
