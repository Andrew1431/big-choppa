using Mono.Cecil;
var asm = AssemblyDefinition.ReadAssembly(args[0]);
var rx = new System.Text.RegularExpressions.Regex(args[1]);
foreach (var t in asm.MainModule.GetTypes()) {
  if (!rx.IsMatch(t.FullName)) continue;
  Console.WriteLine($"== {t.FullName} : {t.BaseType?.FullName}");
  if (args.Length > 2 && args[2] == "names") continue;
  if (t.IsEnum) foreach (var f in t.Fields.Where(f => f.HasConstant)) Console.WriteLine($"  E {f.Name} = {f.Constant}");
  else foreach (var f in t.Fields.Where(f => !f.IsStatic)) Console.WriteLine($"  F {f.FieldType.Name} {f.Name}");
  foreach (var p in t.Properties) Console.WriteLine($"  P {((p.GetMethod?.IsStatic ?? false)?"static ":"")}{p.PropertyType.FullName} {p.Name}");
  foreach (var m in t.Methods) { if (m.Name.StartsWith("get_")||m.Name.StartsWith("set_")||m.Name.StartsWith("Method_")||m.Name==".cctor") continue; Console.WriteLine($"  M {(m.IsStatic?"static ":"")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.Parameters.Select(x=>x.ParameterType.Name+" "+x.Name))})"); }
}
