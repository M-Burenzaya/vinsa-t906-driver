// Seed source-bound Delphi method descriptors before autoanalysis.
// @category MyTablet.Research
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class SeedMyTabletMethods extends GhidraScript {
    public void run() throws Exception {
        String[] args=getScriptArgs();
        if(args.length!=1) throw new IllegalArgumentException("Expected absolute address/name TSV");
        int made=0,existing=0,failed=0;
        for(String line:Files.readAllLines(Paths.get(args[0]),StandardCharsets.UTF_8)) {
            String[] fields=line.split("\\t",2); Address entry=toAddr(fields[0]);
            if(getFunctionAt(entry)!=null) {existing++;continue;}
            disassemble(entry);
            if(createFunction(entry,"published_"+fields[1]+"_"+entry.toString())==null) {failed++;println("Seed failed "+line);}
            else made++;
        }
        println("METHOD SEEDS created="+made+" existing="+existing+" failed="+failed);
    }
}
