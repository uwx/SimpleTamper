# SimpleTamper
Automated introspection using Fody & Linq Expressions

## Usage
See also [Fody usage](https://github.com/Fody/Fody#usage).

### NuGet installation

Install the [HSNXT.SimpleTamper.Fody NuGet package](https://nuget.org/packages/HSNXT.SimpleTamper.Fody/) and update the [Fody NuGet package](https://nuget.org/packages/Fody/):

```
PM> Install-Package HSNXT.SimpleTamper.Fody
PM> Update-Package Fody
```

The `Update-Package Fody` is required since NuGet always defaults to the oldest, and most buggy, version of any dependency.

### Add to FodyWeavers.xml

Add `<HSNXT.SimpleTamper References=""/>` to [FodyWeavers.xml](https://github.com/Fody/Fody#add-fodyweaversxml)
You can include a comma-separated list of assemblies for SimpleTamper to scan, without the file extension.
In Unity for instance, you would want to include `UnityEngine`, `Assembly-CSharp` etc.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Weavers>
  <HSNXT.SimpleTamper References="0Harmony,Assembly-CSharp,UnityEngine,UnityEngine.UI"/>
</Weavers>
```

### Create the shim class

We'll use this class as an example:
```cs
public class Dummy
{
  private float fieldPrivateInstance = 05f;
  private static float fieldPrivateStatic = 05f;
  
  private float propPrivateInstance { get; set; }
  private static float propPrivateStatic { get; set; }
  
  private void InstanceMethod(int arg1, int arg2) { ... }
  private static void StaticMethod(int arg1, int arg2) { ... }
  
  private float InstanceMethodWithReturn(int arg1, int arg2) => default;
  private static float StaticMethodWithReturn(int arg1, int arg2) => default;
}
```

Creating a shim class around it is just a matter of creating a regular class, and the fields/methods for SimpleTamper to fill in.
```cs
[TamperClass(typeof(Dummy))]
public class TamperDummy
{
  // class body goes here
}
```
Use the `TamperClass` attribute to tell SimpleTamper that this is a shim class.
The type inside it is the type you will be introspecting.

#### Non-public instance fields or properties

You can have empty static getter and setter methods that take in the instance as the first parameter, and the value as second.
The method body does not matter.
```cs
  // getter
  public static float fieldPrivateInstance(Dummy instance) => default;
  // setter
  public static void fieldPrivateInstance(Dummy instance, float value) {}
  
  // the same thing works for properties as well
  public static float propPrivateInstance(Dummy instance) => default;
  public static void propPrivateInstance(Dummy instance, float value) {}
```

You can also create an empty constructor that takes the instance as the first and only parameter, and create empty getter/setter
methods or properties to access it. The method body does not matter. Keeping the constructor empty is recommended.
```cs
  // only one of these is needed per class, obviously
  public DummyIntrospector(Dummy instance) {}
  
  // getter
  public float fieldPrivateInstance() => default;
  // setter
  public void fieldPrivateInstance(float value) {}
  
  // works as both getter and setter
  public float fieldPrivateInstance { get; set; }
  
  // once again, properties and fields are interchangeable
  public float propPrivateInstance() => default;
  public void propPrivateInstance(float value) {}
  
  // mapping properties to properties a neat way of doing it
  public float propPrivateInstance { get; set; }
```

#### Non-public instance methods

You can have an empty static method with the same parameters as the method you're trying to call, simply prefixing an instance parameter
to the method. Methods with up to 15 arguments (16 including the instance) are supported.
```cs
  public static void InstanceMethod(Dummy instance, int arg1, int arg2) {}
  public static float InstanceMethodWithReturn(Dummy instance, int arg1, int arg2) => default;
```

You can also use the constructor method as described above the same way.
```cs
  public DummyIntrospector(Dummy instance) {}
  
  public void InstanceMethod(int arg1, int arg2) {}
  public float InstanceMethodWithReturn(int arg1, int arg2) => default;
```


#### Non-public static fields or properties

You can have empty static getter methods and setter methods taking the value as the only parameter.
The method body does not matter.
```cs
  // getter
  public static float fieldPrivateStatic() => default;
  // setter
  public static void fieldPrivateStatic(float value) {}
  
  // the same thing works for properties as well
  public static float propPrivateStatic() => default;
  public static void propPrivateStatic(float value) {}
```

You can also use properties mapping to either fields or properties, and they'll work just as well.
```cs
  public static float fieldPrivateStatic { get; set; }
  public static float propPrivateStatic { get; set; }
```

#### Non-public static methods

You can have an empty static method with the same parameters as the method you're trying to call.
Methods with up to 15 arguments are supported.
```cs
  public static void StaticMethod(Dummy instance, int arg1, int arg2) {}
  public static float StaticMethodWithReturn(Dummy instance, int arg1, int arg2) => default;
```








