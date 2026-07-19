<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>KerbalismContractScienceBridge</AssemblyName>
    <RootNamespace>KerbalismContractScienceBridge</RootNamespace>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <Deterministic>true</Deterministic>
    <DebugType>portable</DebugType>
    <KspRoot Condition="'$(KspRoot)' == ''">$(KSP_ROOT)</KspRoot>
    <HarmonyAssembly Condition="'$(HarmonyAssembly)' == ''">$(KspRoot)\GameData\000_Harmony\0Harmony.dll</HarmonyAssembly>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(KspRoot)\KSP_x64_Data\Managed\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(KspRoot)\KSP_x64_Data\Managed\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(KspRoot)\KSP_x64_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <!-- REVIEW FIX (0.3.1): compiling against the real KSP/Kerbalism files
         showed this is required. Part.Modules / PartModuleList's type graph
         pulls in UnityEngine.EventSystems.IPointerClickHandler, which lives
         in UnityEngine.UI.dll. Without this reference, mcs and (very likely)
         `dotnet build` on Windows both fail with CS0012 on that interface,
         even though the source never references UI types directly. -->
    <Reference Include="UnityEngine.UI">
      <HintPath>$(KspRoot)\KSP_x64_Data\Managed\UnityEngine.UI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="ContractConfigurator">
      <HintPath>$(KspRoot)\GameData\ContractConfigurator\ContractConfigurator.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(HarmonyAssembly)</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <Target Name="CopyToGameData" AfterTargets="Build">
    <MakeDir Directories="$(MSBuildProjectDirectory)\..\GameData\KerbalismContractScienceBridge\Plugins" />
    <Copy SourceFiles="$(TargetPath)"
          DestinationFolder="$(MSBuildProjectDirectory)\..\GameData\KerbalismContractScienceBridge\Plugins" />
  </Target>
</Project>
