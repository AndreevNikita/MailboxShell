# SerializeMethodsAutoBuilder (SMAB)
**ISerializationMethodsBuilder** realization for [NetBinSerializer](https://github.com/AndreevNikita/NetBinSerializer) library.

- Makes difficult types (custom classes and structures) serialization/deserialization possible with NetBinSerializer.


## How to use
Very simple! `Serializer.useMethodsBuilder(new SMAB());` and Serializer will use this AutoBuilder for unknown types.


## Examples

```c#
//Set SMAB as SerializeMethodsBuilder
Serializer.useMethodsBuilder(new SMAB());

//Example object init
MyObject obj = new MyObject();
//...

//Serialization
SerializeStream sstream = new SerializeStream();
Serializer.serialize<MyObject>(sstream, obj);
byte[] data = sstream.getBytes(); //Object bytes

//Deserialization
SerializeStream dstream = new SerializeStream(data);
MyObject dobj = Serializer.deserialize<MyObject>(dstream); //Your deserialized object here

//...
```

Also you can build serialization methods by yourself and use it without Serializer class
```c#
SerializationMethodsBase methods = new SMAB().getSerializationMethods(typeof(MyObject), false /* Enable built methods cache in Serializer */);

MyObject obj = new MyObject();
//...

//Serialization
SerializeStream sstream = new SerializeStream();
methods.serialize(sstream, obj);
byte[] data = sstream.getBytes(); //Object bytes

//Deserialization
SerializeStream dstream = new SerializeStream(data);
MyObject dobj = (MyObject)methods.deserialize(dstream); //Your deserialized object here

//...
```

## Full examples and tests
* [Serialization/deserialization example and test](https://github.com/AndreevNikita/SerializeMethodsAutoBuilder/blob/main/SerializeMethodsAutoBuilder/Test/Program.cs)
* [Nulls and reference cicles test](https://github.com/AndreevNikita/SerializeMethodsAutoBuilder/blob/main/SerializeMethodsAutoBuilder/Test/Test2.cs)