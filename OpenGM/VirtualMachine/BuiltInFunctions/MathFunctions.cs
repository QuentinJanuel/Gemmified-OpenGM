using OpenGM.IO;
using OpenGM.SerializedFiles;
using System.Collections;
using System.Globalization;
using System.Text;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

public static class MathFunctions
{
    [GMLFunction("is_bool")]
    public static object is_bool(object?[] args) => args[0] is bool;

    [GMLFunction("is_real")]
    public static object is_real(object?[] args) => args[0] is int or long or short or double or float;

    [GMLFunction("is_numeric")]
    public static object is_numeric(object?[] args) => is_real([args[0]]).Conv<bool>() || is_bool([args[0]]).Conv<bool>();

    [GMLFunction("is_string")]
    public static object is_string(object?[] args) => args[0] is string;

    [GMLFunction("is_array")]
    public static object is_array(object?[] args) => args[0] is IList;

    [GMLFunction("is_undefined")]
    public static object is_undefined(object?[] args) => args[0] is null;

    // is_int32
    // is_int64
    // is_ptr
    // is_vec3
    // is_vec4
    // is_matrix

    [GMLFunction("is_struct")]
    public static object is_struct(object?[] args) => args[0] is GMLObject;

    // yyAsm

    [GMLFunction("method")]
    public static object? method(object?[] args)
    {
        // seems to always be self, static, or null.
        // https://github.com/YoYoGames/GameMaker-HTML5/blob/develop/scripts/yyVariable.js#L279
        var struct_ref_or_instance_id = args[0];

        var method = args[1] switch
        {
            // pass by id - i really hope built in functions cant be used with method
            int value => new Method(ScriptResolver.ScriptsByIndex[value.Conv<int>() - GMConstants.FIRST_INSTANCE_ID]),
            // pass method directly
            Method value => value,
            // idk
            _ => throw new NotImplementedException($"Don't know what to do with type {args[1]?.GetType().ToString() ?? "null"}")
        };

        if (struct_ref_or_instance_id is null)
        {
            method.inst = null;
        }
        else if (struct_ref_or_instance_id is bool or int or short or long or double or float)
        {
            var num = struct_ref_or_instance_id.Conv<int>();
            if (num == GMConstants.self)
            {
                method.inst = VMExecutor.Self.Self;
            }
            else if (num == GMConstants.@static)
            {
                /*
                 * TODO : there are static functions in DR, but they're never called (vector2/3 add and scale)
                 * just dummy implementing this so it'll run the initialize part of the constructor
                 */
                method.inst = null;
                DebugLog.LogWarning($"Method() called with -16 (static) struct ref - not implemented. ({VMExecutor.Call.CodeName})");
            }
            else
            {
                method.inst = InstanceManager.Find(num);
            }
        }
        else if (struct_ref_or_instance_id is GMLObject gmlo)
        {
            method.inst = gmlo;
        }
        else
        {
            throw new NotImplementedException();
        }

        return method;
    }

    // method_get_index
    // method_get_self

    [GMLFunction("is_method")]
    public static object? is_method(object?[] args) => args[0] is Method;

    // is_nan
    // is_infinity
    // typeof
    // instanceof

    [GMLFunction("array_length")]
    [GMLFunction("array_length_1d")]
    public static object? array_length(object?[] args)
    {
        if (args[0] is not IList array)
        {
            return null;
        }

        return array.Count;
    }

    [GMLFunction("array_length_2d")]
    public static object? array_length_2d(object?[] args)
    {
        // this actually has a second argument for getting the nth element. but its also deprecated so whatever

        if (args[0] is not IList array)
        {
            return null;
        }

        if (array[0] is not IList innerArray)
        {
            return null;
        }

        return innerArray.Count;
    }

    [GMLFunction("array_height_2d")]
    public static object? array_height_2d(object?[] args)
    {
        if (args[0] is not IList array)
        {
            return null;
        }

        return array.Count;
    }

    // @@array_get@@ is the compiler's own name for the same thing: GMS 2.3+ emits it where an array
    // read isn't a plain `a[i]` on a named variable (through a struct member or a chained accessor,
    // which is how tijit engine 2's audio code walks its track array).
    [GMLFunction("@@array_get@@")]
    [GMLFunction("array_get")]
    public static object? array_get(object?[] args)
    {
        var array = args[0].Conv<IList>();
        var index = args[1].Conv<int>();
        return array[index];
    }

    [GMLFunction("array_set")]
    public static object? array_set(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        var index = args[1].Conv<int>();
        var value = args[2];

        VariableResolver.ArraySet(index, value,
            () => variable);
        // cpp errors if not an array. html just makes it an array. we do former

        return null;
    }

    // array_set_pre
    // array_set_post
    // A 2D array here is a list of lists (see array_length_2d), so both of these are just the 1D
    // accessors applied twice -- the outer index picks the row, the inner one the column.

    [GMLFunction("array_get_2D")]
    public static object? array_get_2D(object?[] args)
    {
        var array = args[0].Conv<IList>();
        var i = args[1].Conv<int>();
        var j = args[2].Conv<int>();

        if (i < 0 || i >= array.Count || array[i] is not IList row)
        {
            return null;
        }

        return j < 0 || j >= row.Count ? null : row[j];
    }

    [GMLFunction("array_set_2D")]
    public static object? array_set_2D(object?[] args)
    {
        var array = args[0].Conv<IList>();
        var i = args[1].Conv<int>();
        var j = args[2].Conv<int>();
        var value = args[3];

        // Grow the outer array to reach row i, creating the row if it isn't one yet -- writing past
        // the end is how GML builds a 2D array up in the first place.
        VariableResolver.ArraySet(i, array.Count > i && array[i] is IList ? array[i] : new GMLArray(),
            () => array);

        VariableResolver.ArraySet(j, value, () => array[i]);

        return null;
    }

    // array_set_2D_pre
    // array_set_2D_post

    [GMLFunction("array_equals")]
    public static object? array_equals(object?[] args)
    {
        var first = args[0].Conv<IList>();
        var second = args[1].Conv<IList>();

        return Enumerable.SequenceEqual((IEnumerable<object?>)first, (IEnumerable<object?>)second);
    }

    [GMLFunction("array_create")]
    public static object? array_create(object?[] args)
    {
        var size = args[0].Conv<int>();
        var value = args.ElementAtOrDefault(1) ?? 0;

        var newArray = Enumerable.Repeat(value, size).ToList();

        return newArray;
    }

    [GMLFunction("array_copy")]
    public static object? array_copy(object?[] args)
    {
        // TODO: account for negative values, check if the result is as expected
        var dest = args[0].Conv<IList>();
        var destIndex = args[1].Conv<int>();
        var src = args[2].Conv<IList>();
        var srcIndex = args[3].Conv<int>();
        var length = args[4].Conv<int>();

        var targetLength = destIndex + length;
        while (dest.Count < targetLength)
        {
            dest.Add(null);
        }

        for (var i = 0; i < length; i++)
        {
            dest[destIndex + i] = src[srcIndex + i];
        }

        return null;
    }

    [GMLFunction("array_resize")]
    public static object? array_resize(object?[] args)
    {
        var array_index = args[0].Conv<IList>();
        var new_size = args[1].Conv<int>();
        var oldSize = array_index.Count;

        if (new_size > oldSize)
        {
            var amountToAdd = new_size - oldSize;
            for (var i = 0; i < amountToAdd; i++)
            {
                array_index.Add(0); // cpp uses undefined. html uses 0
            }
        }
        else
        {
            for (var i = new_size; i < oldSize; i++)
            {
                // dont removeat(i) because the indexes will keep changing
                array_index.RemoveAt(new_size);
            }
        }

        return null;
    }

    [GMLFunction("array_push")]
    public static object? array_push(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        for (var i = 1; i < args.Length; i++)
        {
            variable.Add(args[i]);
        }

        return null;
    }

    [GMLFunction("array_pop")]
    public static object? array_pop(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        if (variable.Count == 0)
        {
            return null;
        }

        var end = variable.Count - 1;
        var value = variable[end];
        variable.RemoveAt(end);
        return value;
    }

    [GMLFunction("array_insert")]
    public static object? array_insert(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        var index = args[1].Conv<int>();

        var curLength = variable.Count;

        if (index < 0)
        {
            // -1 means last element, -2 means second last, etc
            index = curLength - index;
        }

        var valueCount = args.Length - 2;

        if (valueCount <= 0)
        {
            throw new Exception();
        }

        var values = args[2..(valueCount + 2)];

        // TODO: check this shit idk man

        if (index == curLength)
        {
            foreach (var item in values)
            {
                variable.Add(item);
            }
            return null;
        }

        var finalIndex = index + valueCount;
        if (finalIndex >= curLength)
        {
            var amountToAdd = finalIndex - curLength;
            for (var i = 0; i < amountToAdd; i++)
            {
                variable.Add(0); // cpp uses undefined. html uses 0
            }
        }

        for (var i = 0; i < valueCount; i++)
        {
            variable.Insert(index + i, values[i]);
        }

        return null;
    }

    [GMLFunction("array_delete")]
    public static object? array_delete(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        var index = args[1].Conv<int>();
        var number = args[2].Conv<int>();

        if (number < 0)
        {
            index += number + 1;
            number = -number;
        }

        for (var i = index; i < index + number; i++)
        {
            variable.RemoveAt(i);
        }

        return null;
    }

    [GMLFunction("array_sort")]
    public static object? array_sort(object?[] args)
    {
        var variable = args[0].Conv<IList>();
        var sorttype_or_function = args[1];

        // TODO: this almost definitely doesn't match the output of gamemaker's array_sort

        // Strings sorted alphabetically with default asc/desc functionality
        // uses qsort

        var tempArray = new object?[variable.Count];
        variable.CopyTo(tempArray, 0);

        Comparison<object?> cmp;

        if (sorttype_or_function is bool)
        {
            var ascending = sorttype_or_function.Conv<bool>();

            cmp = (a, b) =>
            {
                var result = Comparer<object?>.Default.Compare(a, b);
                return ascending ? result : -result;
            };
        }
        else
        {
            VMCode? comparingCode;

            if (sorttype_or_function is int)
            {
                var vmscript = ScriptResolver.ScriptsByIndex[sorttype_or_function.Conv<int>()];
                comparingCode = vmscript.GetCode();
            }
            else
            {
                var method = sorttype_or_function as Method;
                comparingCode = method!.func.GetCode();
            }

            cmp = (a, b) =>
            {
                var result = VMExecutor.ExecuteCode(comparingCode, null, args: [a, b]);
                return result.Conv<int>();
            };
        }

        Array.Sort(tempArray, cmp);

        for (var i = 0; i < tempArray.Length; i++)
        {
            variable[i] = tempArray[i];
        }

        return null;
    }

    // @@array_set_owner@@

    [GMLFunction("random")]
    public static object random(object?[] args)
    {
        var upper = args[0].Conv<double>();
        return GMRandom.fYYRandom() * upper;
    }

    [GMLFunction("random_range")]
    public static object random_range(object?[] args)
    {
        var n1 = args[0].Conv<double>();
        var n2 = args[1].Conv<double>();

        if (n1 == n2)
        {
            return n1;
        }

        if (n1 > n2)
        {
            // swap if wrong order
            (n1, n2) = (n2, n1);
        }

        var rand = GMRandom.fYYRandom();
        var result = n1 + (rand * (n2 - n1));

        // HTML does another call to rand() here, without using the result. Why???

        return result;
    }

    [GMLFunction("irandom")]
    public static object irandom(object?[] args)
    {
        /*
         * JS does not support int64, so the HTML runner uses a single rand() call
         * to create an int32, then calls rand() again to preserve state parity with C++.
         *
         * This means that numbers generated by irandom() are completely different between the runners,
         * but the internal RNG state remains the same.
         *
         * I've tested this returns the same numbers as the C++ runner.
         */

        var n = args[0].Conv<long>();

        var nSign = 1;
        if (n < 0)
        {
            nSign = -1;
        }

        var rand1 = GMRandom.YYRandom();
        var rand2 = GMRandom.YYRandom();
        var doubleSign = Math.Sign(nSign + n);

        var combined = ((long)rand2 << 32) | rand1; // combine rand1 and rand2 into a single Int64
        var anded = combined & 0x7fffffffffffffff;
        return anded % (doubleSign * (nSign + n)) * doubleSign;
    }

    [GMLFunction("irandom_range")]
    public static object irandom_range(object?[] args)
    {
        var lower = args[0].Conv<long>();
        var upper = args[1].Conv<long>();

        long difference;
        if (lower < upper)
        {
            difference = upper - lower;
        }
        else
        {
            difference = lower - upper;
            (lower, upper) = (upper, lower);
        }

        var rand1 = GMRandom.YYRandom();
        var rand2 = GMRandom.YYRandom();
        var doubleSign = Math.Sign(difference + 1);

        var combined = ((long)rand2 << 32) | rand1; // combine rand1 and rand2 into a single Int64
        var anded = combined & 0x7fffffffffffffff;
        return (anded % (doubleSign * (difference + 1)) * doubleSign) + lower;
    }

    [GMLFunction("random_set_seed")]
    public static object? random_set_seed(object?[] args)
    {
        var val = args[0].Conv<uint>();
        GMRandom.InitRandom(val);
        return null;
    }

    [GMLFunction("random_get_seed")]
    public static object? random_get_seed(object?[] args)
    {
        return GMRandom.Seed;
    }

    [GMLFunction("randomise")]
    [GMLFunction("randomize")]
    public static object? randomize(object?[] args)
    {
        // TODO: check this is the same as C++ (this is the HTML implementation)
        var t = (uint)DateTime.Now.Millisecond;
        t = (t & 0xFFFFFFFF) ^ ((t >> 16) & 0xFFFF) ^ ((t << 16) & 0xFFFF0000);
        return GMRandom.InitRandom(t);
    }

    [GMLFunction("abs")]
    public static object abs(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Abs(val);
    }

    [GMLFunction("round")]
    public static object round(object?[] args)
    {
        var num = args[0].Conv<double>();

        // Banker's rounding (half to even) -- captured from GMS 1.4.9999:
        //   round(0.5)=0  round(1.5)=2  round(2.5)=2  round(3.5)=4  round(-1.5)=-2  round(-2.5)=-2
        // which is Math.Round's default, so the mode was never the problem. The two things that
        // were:
        //
        //  - It rounded `(float)num`. Narrowing to single precision first snaps anything within
        //    ~1e-7 of a midpoint *onto* that midpoint, where the to-even rule then sends it the
        //    wrong way: GM answers round(0.5000000001) = 1 and round(2.5000000001) = 3, but as
        //    floats those are exactly 0.5f and 2.5f, which round to 0 and 2.
        //  - It returned `int`. GM returns a real, and games do round() large values --
        //    round(2147483648.7) is 2147483649, which overflowed an int and came back negative.
        var rounded = Math.Round(num, MidpointRounding.ToEven);

        // round(-0.5) is -0.0 in IEEE, and GM prints that as "0" rather than "-0".
        return rounded == 0 ? 0.0 : rounded;
    }

    [GMLFunction("floor")]
    public static object floor(object?[] args)
    {
        var n = args[0].Conv<double>();
        return Math.Floor(n);
    }

    [GMLFunction("ceil")]
    public static object ceil(object?[] args)
    {
        var n = args[0].Conv<double>();
        return Math.Ceiling(n);
    }

    [GMLFunction("sign")]
    public static object sign(object?[] args)
    {
        // TODO : handle NaN somehow????
        var n = args[0].Conv<double>();
        return Math.Sign(n);
    }

    [GMLFunction("frac")]
    public static object? frac(object?[] args)
    {
        var n = args[0].Conv<double>();
        var integral = Math.Truncate(n); // with sign
        return n - integral;
    }

    [GMLFunction("sqrt")]
    public static object sqrt(object?[] args)
    {
        var val = args[0].Conv<double>();

        // TODO : Docs say that values [-epsilon,0) are set to 0, probably added in newer GM versions

        return Math.Sqrt(val);
    }

    [GMLFunction("sqr")]
    public static object sqr(object?[] args)
    {
        var val = args[0].Conv<double>();
        return val * val;
    }

    [GMLFunction("exp")]
    public static object exp(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Exp(val);
    }

    [GMLFunction("ln")]
    public static object ln(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Log(val);
    }

    [GMLFunction("log2")]
    public static object log2(object?[] args)
    {
        var n = args[0].Conv<double>();
        return Math.Log2(n);
    }

    [GMLFunction("log10")]
    public static object log10(object?[] args)
    {
        var n = args[0].Conv<double>();
        return Math.Log10(n);
    }

    [GMLFunction("sin")]
    public static object sin(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Sin(val);
    }

    [GMLFunction("cos")]
    public static object cos(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Cos(val);
    }

    [GMLFunction("tan")]
    public static object tan(object?[] args)
    {
        var val = args[0].Conv<double>();
        return Math.Tan(val);
    }

    [GMLFunction("arcsin")]
    public static object arcsin(object?[] args)
    {
        var x = args[0].Conv<double>(); // in radians

        if (x < -1 || x > 1)
        {
            throw new NotSupportedException($"x is {x}");
        }

        return Math.Asin(x);
    }

    [GMLFunction("arccos")]
    public static object arccos(object?[] args)
    {
        var x = args[0].Conv<double>(); // in radians

        if (x < -1 || x > 1)
        {
            throw new NotSupportedException($"x is {x}");
        }

        return Math.Acos(x);
    }

    [GMLFunction("arctan")]
    public static object arctan(object?[] args)
    {
        var x = args[0].Conv<double>();
        return Math.Atan(x); // radians
    }

    [GMLFunction("arctan2")]
    public static object arctan2(object?[] args)
    {
        // GML's argument order is (y, x), like C's atan2 -- NOT (x, y).
        var y = args[0].Conv<double>();
        var x = args[1].Conv<double>();

        return Math.Atan2(y, x); // radians
    }

    [GMLFunction("dsin")]
    public static object dsin(object?[] args)
    {
        var a = args[0].Conv<double>(); // degrees
        return Math.Sin(a * CustomMath.Deg2Rad);
    }

    [GMLFunction("dcos")]
    public static object dcos(object?[] args)
    {
        var val = args[0].Conv<double>(); // degrees
        return Math.Cos(val * CustomMath.Deg2Rad);
    }

    [GMLFunction("dtan")]
    public static object dtan(object?[] args)
    {
        var a = args[0].Conv<double>(); // degrees
        return Math.Tan(a * CustomMath.Deg2Rad);
    }

    // The d* inverse trig functions take the same argument as their radian counterparts and differ
    // only in returning degrees.

    [GMLFunction("darcsin")]
    public static object darcsin(object?[] args)
    {
        var x = args[0].Conv<double>();
        return Math.Asin(x) * CustomMath.Rad2Deg;
    }

    [GMLFunction("darccos")]
    public static object darccos(object?[] args)
    {
        var x = args[0].Conv<double>();
        return Math.Acos(x) * CustomMath.Rad2Deg;
    }

    [GMLFunction("darctan")]
    public static object darctan(object?[] args)
    {
        var x = args[0].Conv<double>();
        return Math.Atan(x) * CustomMath.Rad2Deg;
    }

    [GMLFunction("darctan2")]
    public static object darctan2(object?[] args)
    {
        var y = args[0].Conv<double>();
        var x = args[1].Conv<double>();

        return Math.Atan2(y, x) * CustomMath.Rad2Deg;
    }

    [GMLFunction("degtorad")]
    public static object degtorad(object?[] args)
    {
        var deg = args[0].Conv<double>();
        return deg * double.Pi / 180;
    }

    [GMLFunction("radtodeg")]
    public static object radtodeg(object?[] args)
    {
        var rad = args[0].Conv<double>();
        return rad * 180 / double.Pi;
    }

    [GMLFunction("power")]
    public static object power(object?[] args)
    {
        var x = args[0].Conv<double>();
        var n = args[1].Conv<double>();

        return Math.Pow(x, n);
    }

    [GMLFunction("logn")]
    public static object logn(object?[] args)
    {
        var n = args[0].Conv<double>();
        var val = args[1].Conv<double>();

        return Math.Log(val, n);
    }

    [GMLFunction("min")]
    public static object min(object?[] args)
    {
        var arguments = new double[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            arguments[i] = args[i].Conv<double>();
        }

        return arguments.Min();
    }

    [GMLFunction("max")]
    public static object max(object?[] args)
    {
        var arguments = new double[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            arguments[i] = args[i].Conv<double>();
        }

        return arguments.Max();
    }

    [GMLFunction("min3")]
    public static object min3(object?[] args) => min(args);

    [GMLFunction("max3")]
    public static object max3(object?[] args) => max(args);

    [GMLFunction("mean")]
    public static object mean(object?[] args)
    {
        if (args.Length == 0)
        {
            return 0;
        }

        var realValues = new double[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            realValues[i] = args[i].Conv<double>();
        }

        return realValues.Average();
    }

    [GMLFunction("median")]
    public static object median(object?[] args)
    {
        if (args.Length == 0)
        {
            return 0;
        }

        var realValues = new double[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            realValues[i] = args[i].Conv<double>();
        }

        Array.Sort(realValues);

        return realValues[CustomMath.FloorToInt(args.Length / 2f)];
    }

    [GMLFunction("choose")]
    public static object? choose(object?[] args)
    {
        return args[GMRandom.YYRandom(args.Length)];
    }

    [GMLFunction("clamp")]
    public static object? clamp(object?[] args)
    {
        var val = args[0].Conv<double>();
        var min = args[1].Conv<double>();
        var max = args[2].Conv<double>();

        // min(max(val, minv), maxv), so that when the bounds are given backwards the *max* wins.
        // Captured from GMS 1.4.9999: clamp(5, 10, 0) is 0, not 10 -- this used to test val <= min
        // first and return the min, which is the opposite answer for every inverted-bounds call.
        return Math.Min(Math.Max(val, min), max);
    }

    [GMLFunction("lerp")]
    public static object? lerp(object?[] args)
    {
        var a = args[0].Conv<double>();
        var b = args[1].Conv<double>();
        var amt = args[2].Conv<double>();

        return a + ((b - a) * amt);
    }

    [GMLFunction("real")]
    public static object real(object?[] args)
    {
        var str = args[0].Conv<string>();
        return str.Conv<double>();
    }

    // bool

    [GMLFunction("string")]
    public static object @string(params object?[] args)
    {
        static string StringifyDict(Dictionary<string, object?> dict)
        {
            var result = "{ ";

            var i = 0;
            foreach (var kv in dict)
            {
                result += $"\"{kv.Key}\": ";

                result += kv.Value switch
                {
                    Dictionary<string, object?> otherDict => StringifyDict(otherDict),
                    _ => @string([kv.Value])
                };

                if (i < dict.Count - 1)
                {
                    result += ", ";
                }

                i++;
            }

            result += " }";
            return result;
        }

        var valueOrFormat = args[0];

        var values = new object?[] { };
        if (args.Length > 1)
        {
            values = args[1..];
        }

        if (args.Length > 1)
        {
            // format
            var format = (string)valueOrFormat!;

            // doing this like im in c lol
            var result = new StringBuilder();
            var bracesString = new StringBuilder();

            var inBraces = false;
            foreach (var formatChar in format)
            {
                if (!inBraces)
                {
                    if (formatChar == '{')
                    {
                        inBraces = true;
                    }
                    else
                    {
                        result.Append(formatChar);
                    }
                }
                else
                {
                    if (formatChar == '}')
                    {
                        inBraces = false;
                        var bracesNumber = int.Parse(bracesString.ToString());
                        bracesString.Clear();
                        result.Append(@string(values[bracesNumber]));
                    }
                    else
                    {
                        bracesString.Append(formatChar);
                    }
                }
            }
            if (inBraces)
            {
                result.Append(bracesString);
            }

            return result.ToString();
        }
        else
        {
            // value

            if (valueOrFormat is IList array)
            {
                // array
                // is any of this right? not sure.
                var index = 0;
                var result = new StringBuilder("[");
                foreach (var item in array)
                {
                    var elementString = (string)@string(item);

                    result.Append(elementString);
                    if (index < array.Count - 1)
                    {
                        result.Append(", ");
                    }
                    index++;
                }

                result.Append("]");
                return result.ToString();
            }
            else if (valueOrFormat is null)
            {
                return "undefined";
            }
            else if (valueOrFormat is bool b)
            {
                return b.Conv<string>();
            }
            else if (valueOrFormat is string s)
            {
                return s;
            }
            else if (valueOrFormat is int or short or long or double or float)
            {
                // real
                var num = valueOrFormat.Conv<double>();
                var afterTwoDigits = num % 0.01f;
                var truncated = num - afterTwoDigits;

                // InvariantCulture, not the machine's: GML's number->string always uses "." for the
                // decimal point regardless of locale, and VMExecutor.Conv's own double->string
                // branch already says so. Without it, a French-locale machine formats 567.4 as
                // "567,4" -- which then fails to parse back through real(), and quietly corrupts
                // any game that builds a number into a string and reads it again.
                return (truncated % 1) == 0
                    ? truncated.ToString(CultureInfo.InvariantCulture)
                    : Math.Round(truncated, 2).ToString(CultureInfo.InvariantCulture);
            }
            else if (valueOrFormat is GMLObject obj)
            {
                return StringifyDict(obj.SelfVariables);
            }
            else if (valueOrFormat is Dictionary<string, object?> dict)
            {
                return StringifyDict(dict);
            }
            else
            {
                return valueOrFormat.ToString() ?? "[unknown]";
            }
        }
    }

    [GMLFunction("int64")]
    public static object int64(object?[] args)
    {
        return args[0].Conv<long>();
    }

    // ptr

    [GMLFunction("string_format")]
    public static object? string_format(object?[] args)
    {
        var val = args[0].Conv<double>();
        var total = args[1].Conv<int>();
        var dec = args[2].Conv<int>();

        // `dec` decimal places, then space-padded on the left to `total` characters. Verified against
        // GameMaker Studio 1.4.9999 (a probe project): string_format(1, 1, 8) is "1.00000000" and
        // string_format(3.9999983310699463, 1, 8) is "3.99999833".
        //
        // This was doing something quite different, and wrong twice over. A whole number returned
        // early with no decimals at all ("1" instead of "1.00000000"), and everything else took a
        // *substring of the fractional part's decimal text*, which falls apart the moment that text
        // isn't a plain 0.xxx: for 4.000000000000002 the fractional part stringifies as
        // "1.7763568394002505E-15", and characters 2..9 of that are "77635683", so the answer came
        // back as "4.77635683". Small fractional parts are exactly what you get from accumulating a
        // float, so this misreported image_index, timers, and anything else built by addition.
        var result = val.ToString("F" + CustomMath.Max(dec, 0), CultureInfo.InvariantCulture);

        return result.PadLeft(total);
    }

    [GMLFunction("chr")]
    public static object chr(object?[] args)
    {
        var val = args[0].Conv<int>();

        // TODO : "This character depends on the current drawing fonts character set code page and if no font is set, it will use the default code page for the machine."
        // what the fuck does this mean

        /*var currentFont = TextManager.fontAsset;

        if (currentFont.entriesDict.ContainsKey(val))
        {
            return Convert.ToChar(val).ToString();
        }
        else
        {
            throw new NotImplementedException();
        }*/

        return Convert.ToChar(val).ToString();
    }

    // ansi_char

    [GMLFunction("ord")]
    public static object ord(object?[] args)
    {
        var str = args[0].Conv<string>();

        return (int)Encoding.UTF8.GetBytes(str)[0];
    }

    [GMLFunction("string_length")]
    public static object string_length(object?[] args)
    {
        var str = args[0].Conv<string>();

        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        return str.Length;
    }

    /// <summary>
    /// The length in BYTES rather than characters -- Studio's strings are UTF-8, so this is only
    /// the same as <see cref="string_length"/> for pure-ASCII text.
    /// </summary>
    [GMLFunction("string_byte_length")]
    public static object string_byte_length(object?[] args)
    {
        var str = args[0].Conv<string>();

        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        return Encoding.UTF8.GetByteCount(str);
    }

    /// <summary>The <paramref name="args"/>[1]'th byte (1-based) of the string's UTF-8 encoding, or 0 if out of range.</summary>
    [GMLFunction("string_byte_at")]
    public static object string_byte_at(object?[] args)
    {
        var str = args[0].Conv<string>();
        var index = args[1].Conv<int>();

        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(str);

        // Real GM reads whatever happens to be at that address for an out-of-range index; return 0
        // instead, matching what OpenGMK settled on for the same function.
        if (index < 1 || index > bytes.Length)
        {
            return 0;
        }

        return (int)bytes[index - 1];
    }

    /// <summary>The single character for a byte value, i.e. <c>chr</c> over the ANSI (Latin-1) code page.</summary>
    [GMLFunction("ansi_char")]
    public static object ansi_char(object?[] args)
    {
        var val = args[0].Conv<int>();
        return ((char)(byte)val).ToString();
    }

    [GMLFunction("string_pos")]
    public static object string_pos(object?[] args)
    {
        var substr = args[0].Conv<string>();
        var str = args[1].Conv<string>();

        return str.IndexOf(substr) + 1;
    }

    [GMLFunction("string_pos_ext")]
    public static object string_pos_ext(object?[] args)
    {
        var substr = args[0].Conv<string>();
        var str = args[1].Conv<string>();
        var start_pos = args[2].Conv<int>();

        return str.IndexOf(substr, start_pos - 1) + 1;
    }

    // string_last_pos
    // string_last_pos_ext

    [GMLFunction("string_copy")]
    public static object string_copy(object?[] args)
    {
        var str = args[0].Conv<string>();
        var index = args[1].Conv<int>();
        var count = args[2].Conv<int>();

        if (index < 1)
        {
            index = 1;
        }

        var maxLength = str.Length - (index - 1);

        // no idea if this is what GM does, but i THINK it is
        if (count > maxLength)
        {
            count = maxLength;
        }

        return str.Substring(index - 1, count);
    }

    [GMLFunction("string_char_at")]
    public static object string_char_at(object?[] args)
    {
        var str = args[0].Conv<string>();
        var index = args[1].Conv<int>();

        if (string.IsNullOrEmpty(str) || index > str.Length)
        {
            return "";
        }

        if (index <= 0)
        {
            return str[0].ToString();
        }

        // guh index starts at one? goofy gamemaker
        return str[index - 1].ToString();
    }

    [GMLFunction("string_ord_at")]
    public static object string_ord_at(object?[] args)
    {
        var str = args[0].Conv<string>();
        var index = args[1].Conv<int>();

        if (string.IsNullOrEmpty(str) || index > str.Length)
        {
            return -1;
        }

        if (index <= 0)
        {
            return (int)str[0];
        }

        return (int)str[index - 1];
    }

    // string_byte_length
    // string_byte_at
    // string_set_byte_at

    [GMLFunction("string_delete")]
    public static object string_delete(object?[] args)
    {
        var str = args[0].Conv<string>();
        var index = args[1].Conv<int>();
        var count = args[2].Conv<int>();

        return str.Remove(index - 1, count);
    }

    [GMLFunction("string_insert")]
    public static object string_insert(object?[] args)
    {
        var substr = args[0].Conv<string>();
        var str = args[1].Conv<string>();
        var index = args[2].Conv<int>();

        return str.Insert(Math.Min(index - 1, str.Length), substr);
    }

    [GMLFunction("string_lower")]
    public static object string_lower(object?[] args)
    {
        var str = args[0].Conv<string>();
        // TODO : only do the 26 english alphabet letters
        return str.ToLower();
    }

    [GMLFunction("string_upper")]
    public static object string_upper(object?[] args)
    {
        var str = args[0].Conv<string>();
        // TODO : only do the 26 english alphabet letters
        return str.ToUpper();
    }

    [GMLFunction("string_repeat")]
    public static object? string_repeat(object?[] args)
    {
        var str = args[0].Conv<string>();
        var count = args[1].Conv<int>();

        var ret = "";
        for (var i = 0; i < count; i++)
        {
            ret += str;
        }

        return ret;
    }

    [GMLFunction("string_letters")]
    public static object string_letters(object?[] args)
    {
        var str = args[0].Conv<string>();

        var result = "";

        foreach (var c in str)
        {
            if (char.IsAsciiLetter(c))
            {
                result += c;
            }
        }

        return result;
    }

    [GMLFunction("string_digits")]
    public static object string_digits(object?[] args)
    {
        var str = args[0].Conv<string>();

        var result = "";

        foreach (var c in str)
        {
            if (char.IsAsciiDigit(c))
            {
                result += c;
            }
        }

        return result;
    }

    [GMLFunction("string_lettersdigits")]
    public static object string_lettersdigits(object?[] args)
    {
        var str = args[0].Conv<string>();

        var result = "";

        foreach (var c in str)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                result += c;
            }
        }

        return result;
    }

    [GMLFunction("string_replace")]
    public static object string_replace(object?[] args)
    {
        var str = args[0].Conv<string>();
        var substr = args[1].Conv<string>();
        var newstr = args[2].Conv<string>();

        var pos = str.IndexOf(substr);
        if (pos == -1)
        {
            return str;
        }

        return string.Concat(str.AsSpan(0, pos), newstr, str.AsSpan(pos + substr.Length));
    }

    [GMLFunction("string_replace_all")]
    public static object string_replace_all(object?[] args)
    {
        var str = args[0].Conv<string>();
        var substr = args[1].Conv<string>();
        var newstr = args[2].Conv<string>();

        return str.Replace(substr, newstr);
    }

    [GMLFunction("string_trim")]
    public static object string_trim(object?[] args)
    {
        var str = args[0].Conv<string>();

        if (args.Length == 1)
        {
            return str.Trim();
        }

        // TODO: this probably sucks
        var list = (IEnumerable<string>)args[1]!;
        var sub = "";
        for (; ; )
        {
            sub = list.FirstOrDefault(s => str.StartsWith(s) || str.EndsWith(s));
            if (sub is null)
            {
                break;
            }

            if (sub.Length == 0)
            {
                throw new ArgumentException("Empty substring");
            }

            while (str.StartsWith(sub))
            {
                str = str[sub.Length..];
            }

            while (str.EndsWith(sub))
            {
                str = str[..^sub.Length];
            }
        }

        return str;
    }

    [GMLFunction("string_count")]
    public static object string_count(object?[] args)
    {
        var substr = args[0].Conv<string>();
        var str = args[1].Conv<string>();

        var count = 0;
        var index = 0;

        while ((index = str.IndexOf(substr, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substr.Length;
        }

        return count;
    }

    [GMLFunction("string_hash_to_newline")]
    public static object string_hash_to_newline(object?[] args)
    {
        var text = args[0].Conv<string>();

        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace('#', '\n');
    }

    [GMLFunction("md5_string_unicode")]
    public static object md5_string_unicode(object?[] args)
    {
        var text = args[0].Conv<string>();
        var bytes = System.Text.Encoding.Unicode.GetBytes(text); // GM8's "unicode" = UTF-16LE
        var hash = MD5Managed.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    [GMLFunction("md5_string_utf8")]
    public static object md5_string_utf8(object?[] args)
    {
        var text = args[0].Conv<string>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = MD5Managed.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    [GMLFunction("base64_encode")]
    public static object base64_encode(object?[] args)
    {
        var text = args[0].Conv<string>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes);
    }

    [GMLFunction("base64_decode")]
    public static object base64_decode(object?[] args)
    {
        var text = args[0].Conv<string>();
        var bytes = Convert.FromBase64String(text);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    [GMLFunction("point_distance_3d")]
    public static object point_distance_3d(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var z1 = args[2].Conv<double>();
        var x2 = args[3].Conv<double>();
        var y2 = args[4].Conv<double>();
        var z2 = args[5].Conv<double>();

        var dx = x2 - x1;
        var dy = y2 - y1;
        var dz = z2 - z1;

        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// The plain dot product, NOT the normalised one -- GameMaker Studio splits the two into
    /// separate functions (<c>dot_product_normalised</c> below), unlike GM8, whose single
    /// <c>dot_product</c> normalised its inputs first (see OpenGMK's own gml/kernel.rs, which
    /// reimplements GM8 rather than Studio).
    /// </summary>
    [GMLFunction("dot_product")]
    public static object dot_product(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var x2 = args[2].Conv<double>();
        var y2 = args[3].Conv<double>();

        return (x1 * x2) + (y1 * y2);
    }

    [GMLFunction("dot_product_3d")]
    public static object dot_product_3d(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var z1 = args[2].Conv<double>();
        var x2 = args[3].Conv<double>();
        var y2 = args[4].Conv<double>();
        var z2 = args[5].Conv<double>();

        return (x1 * x2) + (y1 * y2) + (z1 * z2);
    }

    /// <summary>Scales both vectors to unit length first, so the result is the cosine of the angle between them.</summary>
    [GMLFunction("dot_product_normalised")]
    public static object dot_product_normalised(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var x2 = args[2].Conv<double>();
        var y2 = args[3].Conv<double>();

        var l1 = Math.Sqrt((x1 * x1) + (y1 * y1));
        var l2 = Math.Sqrt((x2 * x2) + (y2 * y2));

        // A zero-length vector has no direction to compare; GM's own divide-by-zero would give a
        // NaN that then poisons whatever the caller does with it.
        if (l1 == 0 || l2 == 0)
        {
            return 0d;
        }

        return (((x1 / l1) * (x2 / l2)) + ((y1 / l1) * (y2 / l2)));
    }

    [GMLFunction("dot_product_3d_normalised")]
    public static object dot_product_3d_normalised(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var z1 = args[2].Conv<double>();
        var x2 = args[3].Conv<double>();
        var y2 = args[4].Conv<double>();
        var z2 = args[5].Conv<double>();

        var l1 = Math.Sqrt((x1 * x1) + (y1 * y1) + (z1 * z1));
        var l2 = Math.Sqrt((x2 * x2) + (y2 * y2) + (z2 * z2));

        if (l1 == 0 || l2 == 0)
        {
            return 0d;
        }

        return (((x1 / l1) * (x2 / l2)) + ((y1 / l1) * (y2 / l2)) + ((z1 / l1) * (z2 / l2)));
    }

    [GMLFunction("point_distance")]
    public static object point_distance(object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var x2 = args[2].Conv<double>();
        var y2 = args[3].Conv<double>();

        var horizDistance = Math.Abs(x2 - x1);
        var vertDistance = Math.Abs(y2 - y1);

        return Math.Sqrt((horizDistance * horizDistance) + (vertDistance * vertDistance));
    }

    [GMLFunction("point_direction")]
    public static object point_direction(params object?[] args)
    {
        var x1 = args[0].Conv<double>();
        var y1 = args[1].Conv<double>();
        var x2 = args[2].Conv<double>();
        var y2 = args[3].Conv<double>();

        var deltaX = x2 - x1;
        var deltaY = y2 - y1;

        return ((-Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI) + 360) % 360;
    }

    [GMLFunction("lengthdir_x")]
    public static object? lengthdir_x(object?[] args)
    {
        var len = args[0].Conv<double>();
        var dir = args[1].Conv<double>();

        return len * Math.Cos(dir * CustomMath.Deg2Rad);
    }

    [GMLFunction("lengthdir_y")]
    public static object? lengthdir_y(object?[] args)
    {
        var len = args[0].Conv<double>();
        var dir = args[1].Conv<double>();

        return -len * Math.Sin(dir * CustomMath.Deg2Rad);
    }

    // point_distance_3d
    // dot_product
    // dot_product_normalised
    // dot_product_3d
    // dot_product_3d_normalised
    // math_set_epsilon
    // math_get_epsilon

    [GMLFunction("angle_difference")]
    public static object? angle_difference(object?[] args)
    {
        var dest = args[0].Conv<double>();
        var src = args[1].Conv<double>();
        return CustomMath.Mod(dest - src + 180, 360) - 180;
    }

    // weak_ref_create
    // weak_ref_alive
    // weak_ref_any_alive
}
