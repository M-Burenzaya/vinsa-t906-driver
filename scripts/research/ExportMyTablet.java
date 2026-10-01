// Whole-program static export. No vendor code is executed.
// @category MyTablet.Research
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import ghidra.program.model.address.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

public class ExportMyTablet extends GhidraScript {
    private final Gson json = new GsonBuilder().setPrettyPrinting().create();
    private void write(Path path, String value) throws Exception {
        Files.writeString(path, value, StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
    }
    private Map<String,Object> row(Object... values) {
        Map<String,Object> result = new LinkedHashMap<>();
        for (int i=0;i<values.length;i+=2) result.put((String)values[i],values[i+1]);
        return result;
    }
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) throw new IllegalArgumentException("Expected a fresh absolute output folder");
        Path output = Paths.get(args[0]);
        if (!output.isAbsolute()) throw new IllegalArgumentException("Output must be absolute");
        Files.createDirectories(output);
        Path pseudo = output.resolve("pseudocode"), assembly = output.resolve("assembly");
        Files.createDirectories(pseudo); Files.createDirectories(assembly);
        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true); decompiler.toggleSyntaxTree(false);
        decompiler.openProgram(currentProgram);
        List<Map<String,Object>> functions = new ArrayList<>();
        int succeeded=0,failed=0,external=0;
        try {
            FunctionIterator iterator = currentProgram.getFunctionManager().getFunctions(true);
            while(iterator.hasNext()) {
                monitor.checkCancelled();
                Function f=iterator.next(); String address=f.getEntryPoint().toString();
                String stem=address.replace(':','_');
                List<Map<String,Object>> ranges=new ArrayList<>();
                for(AddressRange range:f.getBody().getAddressRanges()) ranges.add(row("start",range.getMinAddress().toString(),"end_inclusive",range.getMaxAddress().toString()));
                Map<String,Object> entry=row("address",address,"name",f.getName(),"signature",f.getSignature().toString(),"external",f.isExternal(),"thunk",f.isThunk(),"ranges",ranges,"body_bytes",f.getBody().getNumAddresses());
                Set<String> callees=new TreeSet<>();
                StringBuilder asm=new StringBuilder("; "+f.getName()+" @ "+address+"\n");
                InstructionIterator instructions=currentProgram.getListing().getInstructions(f.getBody(),true);
                int instructionCount=0;
                while(instructions.hasNext()) {
                    Instruction instruction=instructions.next(); instructionCount++;
                    asm.append(instruction.getAddress()).append("  ");
                    for(byte b:instruction.getBytes()) asm.append(String.format("%02x ",b&255));
                    asm.append(" ").append(instruction.toString()).append('\n');
                    for(Reference ref:instruction.getReferencesFrom()) if(ref.getReferenceType().isCall()) callees.add(ref.getToAddress().toString());
                }
                entry.put("instructions",instructionCount); entry.put("static_call_targets",callees);
                write(assembly.resolve(stem+".asm"),asm.toString());
                if(f.isExternal()) {entry.put("decompile_status","external");external++;}
                else {
                    DecompileResults result=decompiler.decompileFunction(f,30,monitor);
                    if(result.decompileCompleted() && result.getDecompiledFunction()!=null) {
                        write(pseudo.resolve(stem+".c"),"/* Ghidra pseudocode, not original Delphi/C source. Entry "+address+". */\n"+result.getDecompiledFunction().getC());
                        entry.put("decompile_status","ok"); entry.put("pseudocode_path","pseudocode/"+stem+".c"); succeeded++;
                    } else {entry.put("decompile_status","failed");entry.put("error",result.getErrorMessage());failed++;}
                }
                functions.add(entry);
                if(functions.size()%100==0) println("Exported "+functions.size()+" functions; decompiled "+succeeded+"; failed "+failed);
            }
        } finally {decompiler.dispose();}
        List<Map<String,Object>> symbols=new ArrayList<>();
        SymbolIterator symbolIterator=currentProgram.getSymbolTable().getAllSymbols(true);
        while(symbolIterator.hasNext()) {Symbol s=symbolIterator.next(); symbols.add(row("address",s.getAddress().toString(),"name",s.getName(true),"kind",s.getSymbolType().toString(),"source",s.getSource().toString(),"external",s.isExternal()));}
        List<Map<String,Object>> strings=new ArrayList<>();
        DataIterator dataIterator=currentProgram.getListing().getDefinedData(true);
        while(dataIterator.hasNext()) {Data data=dataIterator.next();if(data.hasStringValue()) strings.add(row("address",data.getAddress().toString(),"type",data.getDataType().getName(),"value",data.getValue().toString(),"bytes",data.getLength()));}
        write(output.resolve("functions.json"),json.toJson(functions));
        write(output.resolve("symbols.json"),json.toJson(symbols));
        write(output.resolve("strings.json"),json.toJson(strings));
        write(output.resolve("summary.json"),json.toJson(row("binary",currentProgram.getName(),"executable_sha256",currentProgram.getExecutableSHA256(),"language",currentProgram.getLanguageID().toString(),"compiler",currentProgram.getCompilerSpec().getCompilerSpecID().toString(),"functions",functions.size(),"decompiled",succeeded,"failed",failed,"external",external,"symbols",symbols.size(),"strings",strings.size(),"limitations",List.of("Automatically discovered function boundaries; undiscovered code is not included.","Decompiler pseudocode is not original source and may have incorrect types or calling conventions.","Export is static; no vendor executable or hardware command was run.","Per-function decompiler timeout is 30 seconds."))));
        println("FINAL functions="+functions.size()+" decompiled="+succeeded+" failed="+failed);
    }
}
