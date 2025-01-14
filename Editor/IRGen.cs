using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;

namespace UdonFlat {
public enum Opcode {
	EXTERN, // arg0: signature, args: parameters
	CALL, // arg0: address
	JUMP, // arg0: address, args: falseCond
	EXIT, RETURN, // args: falseCond
}
public class Instruction {
	public string addr;
	public Opcode opcode;
	public string arg0;
	public string[] args;
	public override string ToString() {
		return $"{addr}\t{opcode}\t{arg0}\t{(args == null ? null : string.Join("\t", args))}";
	}
}
public class IRGen {
	public IUdonProgram program;
	public Disassembler asm;
	public static string FormatAddr(uint addr) => $"0x{addr:X8}";
	public static uint ParseAddr(string addr) => System.Convert.ToUInt32(addr, 16);

	bool[] visited;
	Instruction[] translated;
	void Translate() {
		visited = new bool[asm.code.Length];
		translated = new Instruction[asm.code.Length];
		foreach(var name in program.EntryPoints.GetSymbols())
			Translate(asm.lineFromAddr[FormatAddr(program.EntryPoints.GetAddressFromSymbol(name))],
				new Stack<string>());
	}
	void Translate(int line, Stack<string> stack) {
		var prevLine = line;
		while(line < asm.code.Length && !visited[line]) {
			visited[line] = true;

			var instr = asm.code[line];
			var addr = instr[0];
			var opcode = instr[1];
			var nextLine = line+1;
			
			if(opcode == "POP") {
				stack.Pop();
			} else if(opcode == "PUSH") {
				var name = program.SymbolTable.GetSymbolFromAddress(ParseAddr(instr[2]));
				stack.Push(name);
			} else if(opcode == "COPY") {
				var targetName = stack.Pop();
				var sourceName = stack.Count > 0 ? stack.Pop() : null;
				if(sourceName != targetName) // optimization: skip no-op
					translated[line] = new Instruction{addr=addr, // express COPY as EXTERN
						opcode=Opcode.EXTERN, arg0=StatGen.COPY, args=new[]{sourceName, targetName}};
			} else if(opcode == "EXTERN") {
				var signature = instr[2].Trim('"');
				var nodeDef = UdonEditorManager.Instance.GetNodeDefinition(signature);
				var paramNames = new string[nodeDef.parameters.Count];
				for(int i=nodeDef.parameters.Count-1; i>=0; i--)
					paramNames[i] = stack.Pop();
				translated[line] = new Instruction{addr=addr, opcode=Opcode.EXTERN, arg0=signature, args=paramNames};
			} else if(opcode == "JUMP" || opcode == "JUMP_IF_FALSE") {
				var jumpAddr = instr[2];
				// match CALL pattern {PUSH label; ....; JUMP xxx; label:}
				var isCall = opcode == "JUMP" && nextLine < asm.code.Length && stack.Count > 0
					&& ParseAddr(asm.code[nextLine][0]).Equals(
						program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(stack.Peek())));
				if(isCall) {
					translated[line] = new Instruction{addr=addr, opcode=Opcode.CALL, arg0=jumpAddr};
					Translate(asm.lineFromAddr[jumpAddr], Clone(stack)); // explore hidden entry point
					stack.Pop();
				} else {
					var cond = opcode == "JUMP_IF_FALSE" ? new[]{stack.Pop()} : null;
					// out-of-bound address means EXIT
					if(!asm.lineFromAddr.TryGetValue(jumpAddr, out var jumpLine)) {
						translated[line] = new Instruction{addr=addr, opcode=Opcode.EXIT, args=cond};
						if(cond == null)
							return;
					} else if(jumpLine != nextLine) { // optimization: skip no-op
						translated[line] = new Instruction{addr=addr, opcode=Opcode.JUMP, arg0=jumpAddr, args=cond};
						if(cond == null)
							nextLine = jumpLine;
						else
							Translate(jumpLine, Clone(stack)); // explore branch
					}
				}
			} else if(opcode == "JUMP_INDIRECT") {
				var jumpName = instr[2];
				// match RETURN pattern {PUSH addr; COPY; JUMP_INDIRECT addr}
				var isReturn = asm.code[prevLine][1] == "COPY" && translated[prevLine] != null
					&& translated[prevLine].args[1] == jumpName;
				if(isReturn)
					translated[prevLine] = null;
				Debug.Assert(isReturn);
				translated[line] = new Instruction{addr=addr, opcode=Opcode.RETURN};
				return;
			} else {
				Debug.Assert(opcode == "NOP" || opcode == "ANNOTATION");
			}
			prevLine = line;
			line = nextLine;
		}
	}
	static Stack<string> Clone(Stack<string> stack) {
		return new Stack<string>(stack.Reverse());
	}

	public Instruction[] code;
	public Dictionary<string, int> lineFromAddr;
	void CollapseLines() {
		var lineMap = new int[translated.Length];
		code = new Instruction[translated.Count(x => x != null)];
		for(int i=code.Length, line=translated.Length-1; line>=0; line--) {
			if(translated[line] != null)
				code[--i] = translated[line];
			lineMap[line] = i;
		}
		lineFromAddr = new Dictionary<string, int>();
		for(int line=0; line<asm.code.Length; line++)
			lineFromAddr[asm.code[line][0]] = lineMap[line];
	}
	void CollapseJumps() {
		for(int line=code.Length-1; line>=0; line--) {
			var instr = code[line];
			if(instr.opcode == Opcode.JUMP) {
				var jumpInstr = code[lineFromAddr[instr.arg0]];
				if((jumpInstr.opcode == Opcode.JUMP || jumpInstr.opcode == Opcode.EXIT || jumpInstr.opcode == Opcode.RETURN) && jumpInstr.args == null) {
					instr.opcode = jumpInstr.opcode;
					instr.arg0 = jumpInstr.arg0;
				}
			}
		}
	}	
	public void Generate() {
		Translate();
		CollapseLines();
		// CollapseJumps(); // TODO: refactoring works better without this
	}
}
}